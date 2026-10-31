using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Match.Protocol;

// The JSON form of Rules.Core commands and events, and of the protocol's own messages. Rules.Core knows nothing about
// JSON: everything the wire needs is configured here.
//
// Every object is written as the parameters of its public constructor and read back through that same constructor, so a
// value the constructor refuses is refused on the wire too. Reading is strict: a field the constructor does not take, a
// field given twice, a missing field without a default, or null where the type says not null is an error. A command
// that carries dice, an outcome or a hash is therefore refused, not quietly trimmed.
public static class Wire
{
    private static readonly Assembly RulesCore = typeof(Command).Assembly;

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = ProtocolLimits.MaxDepth,
            NumberHandling = JsonNumberHandling.Strict,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
        };
        options.Converters.Add(new LengthConverter());
        options.Converters.Add(new ModelIdConverter());
        options.Converters.Add(new FootprintConverter());
        options.Converters.Add(new StrictEnumConverterFactory());
        options.Converters.Add(new PolymorphicConverter<Command>(versioned: false));
        options.Converters.Add(new PolymorphicConverter<GameEvent>(versioned: false));
        options.Converters.Add(new PolymorphicConverter<ClientMessage>(versioned: true));
        options.Converters.Add(new PolymorphicConverter<ServerMessage>(versioned: true));
        options.Converters.Add(new ConstructorConverterFactory());
        options.MakeReadOnly(populateMissingResolver: false);
        return options;
    }

    // Thrown for a value that is well-formed JSON but not a legal value: a die of 7, a point beyond the table limit, an
    // enum name that does not exist. Anything else that fails to read is malformed.
    public sealed class InvalidValueException(string message, Exception? inner = null) : JsonException(message, inner);

    internal static bool IsInvalidValue(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is InvalidValueException or ArgumentException)
            {
                return true;
            }
        }
        return false;
    }

    // ---- Scalars ----

    // A length is an integer number of thousandths of an inch: exact, and the same on every machine.
    private sealed class LengthConverter : JsonConverter<Length>
    {
        public override Length Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var value))
            {
                throw new JsonException("A length is a whole number of thousandths of an inch.");
            }
            return Construct(() => Length.FromThousandths(value));
        }

        public override void Write(Utf8JsonWriter writer, Length value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Thousandths);
    }

    private sealed class ModelIdConverter : JsonConverter<ModelId>
    {
        public override ModelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("A model id is a string.");
            }
            var text = reader.GetString()!;
            return Construct(() => new ModelId(text));
        }

        public override void Write(Utf8JsonWriter writer, ModelId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    // A one-rectangle footprint travels as its rectangle, as since M3. Joined parts (trench-maps stage S2) travel as an
    // array of rectangles, rebuilt and checked through Footprint.Join.
    private sealed class FootprintConverter : JsonConverter<Footprint>
    {
        public override Footprint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                // An ImmutableArray, not Rect[]: an array of a Rules.Core type would be taken for a Rules.Core object.
                var parts = JsonSerializer.Deserialize<ImmutableArray<Rect>>(ref reader, options);
                if (parts.IsDefault || parts.Length < 2)
                {
                    throw new JsonException("A footprint of one rectangle travels as that rectangle.");
                }
                return Construct(() => Footprint.Join(parts));
            }
            var rect = JsonSerializer.Deserialize<Rect>(ref reader, options);
            return Construct(() => Footprint.Of(rect));
        }

        public override void Write(Utf8JsonWriter writer, Footprint value, JsonSerializerOptions options)
        {
            if (value.IsSingle)
            {
                JsonSerializer.Serialize(writer, value.Bounds, options);
                return;
            }
            JsonSerializer.Serialize(writer, value.Parts, options);
        }
    }

    // Enums travel by exact name only: no numbers and no case folding. A [Flags] enum is an array of the names of its set
    // flags, in value order; none set is an empty array.
    private sealed class StrictEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class StrictEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly Dictionary<string, T> ByName =
            Enum.GetValues<T>().ToDictionary(v => v.ToString(), v => v, StringComparer.Ordinal);

        private static readonly bool IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), inherit: false);

        // The single-bit flags, in value order.
        private static readonly T[] Flags = Enum.GetValues<T>()
            .Where(v => Convert.ToUInt64(v) is var bits && bits != 0 && (bits & (bits - 1)) == 0)
            .OrderBy(v => Convert.ToUInt64(v))
            .ToArray();

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (!IsFlags)
            {
                return ReadName(ref reader);
            }
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException($"A {typeof(T).Name} is an array of flag names.");
            }
            ulong bits = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var flag = Convert.ToUInt64(ReadName(ref reader));
                if (!Flags.Any(f => Convert.ToUInt64(f) == flag) || (bits & flag) != 0)
                {
                    throw new InvalidValueException($"Not a set of {typeof(T).Name} flags.");
                }
                bits |= flag;
            }
            return (T)Enum.ToObject(typeof(T), bits);
        }

        private static T ReadName(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"A {typeof(T).Name} is written by name.");
            }
            return ByName.TryGetValue(reader.GetString()!, out var value)
                ? value
                : throw new InvalidValueException($"Not a {typeof(T).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (!IsFlags)
            {
                writer.WriteStringValue(Enum.IsDefined(value) ? value.ToString() : throw new JsonException($"Undefined {typeof(T).Name}."));
                return;
            }
            var bits = Convert.ToUInt64(value);
            var set = Flags.Where(f => (bits & Convert.ToUInt64(f)) != 0).ToArray();
            if (set.Aggregate(0UL, (all, f) => all | Convert.ToUInt64(f)) != bits)
            {
                throw new JsonException($"Undefined {typeof(T).Name} flags.");
            }
            writer.WriteStartArray();
            foreach (var flag in set)
            {
                writer.WriteStringValue(flag.ToString());
            }
            writer.WriteEndArray();
        }
    }

    // ---- Objects ----

    // Commands, events and messages are written with their class name first, under "type"; a message has the protocol
    // version before that, under "v". On reading, they must come first, in that order.
    private sealed class PolymorphicConverter<TBase>(bool versioned) : JsonConverter<TBase> where TBase : class
    {
        private readonly Dictionary<string, Shape> _byName = typeof(TBase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TBase)) && !t.IsAbstract)
            .ToDictionary(t => t.Name, Shape.Of, StringComparer.Ordinal);

        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(TBase);

        public override TBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"A {typeof(TBase).Name} is an object.");
            }
            if (versioned)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("v"u8))
                {
                    throw new JsonException("A message starts with its protocol version, \"v\".");
                }
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var version) || version != ProtocolLimits.Version)
                {
                    throw new UnsupportedVersionException();
                }
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("type"u8))
            {
                throw new JsonException($"A {typeof(TBase).Name} starts with its \"type\".");
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("The \"type\" is a string.");
            }
            var name = reader.GetString()!;
            if (!_byName.TryGetValue(name, out var shape))
            {
                throw new UnknownTypeException(typeof(TBase).Name, name);
            }
            return (TBase)shape.ReadMembers(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
        {
            var type = value.GetType();
            if (!_byName.TryGetValue(type.Name, out var shape) || shape.Type != type)
            {
                throw new JsonException($"{type.Name} is not a {typeof(TBase).Name} the wire knows.");
            }
            writer.WriteStartObject();
            if (versioned)
            {
                writer.WriteNumber("v"u8, ProtocolLimits.Version);
            }
            writer.WriteString("type"u8, type.Name);
            shape.WriteMembers(writer, value, options);
            writer.WriteEndObject();
        }
    }

    public sealed class UnsupportedVersionException() : JsonException($"This server speaks protocol version {ProtocolLimits.Version}.");

    public sealed class UnknownTypeException(string kind, string name) : JsonException($"Unknown {kind} type.")
    {
        public string Kind { get; } = kind;
        public string Name { get; } = name;
    }

    // Any other class or struct from Rules.Core or this assembly.
    private sealed class ConstructorConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            (typeToConvert.Assembly == RulesCore || typeToConvert.Assembly == typeof(Wire).Assembly)
            && !typeToConvert.IsEnum
            && !typeToConvert.IsAbstract
            && !typeToConvert.IsInterface
            && (typeToConvert.IsClass || typeToConvert.IsValueType);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ConstructorConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class ConstructorConverter<T> : JsonConverter<T>
    {
        private readonly Shape _shape = Shape.Of(typeof(T));

        public override bool HandleNull => false;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"A {typeof(T).Name} is an object.");
            }
            return (T)_shape.ReadMembers(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            _shape.WriteMembers(writer, value!, options);
            writer.WriteEndObject();
        }
    }

    // A type's public constructor and, for each parameter, the property of the same name that is written in its place.
    // The field is named after the parameter. A public init property the constructor does not take (such as
    // RangedAttackDeclared.IntoMelee) is written after them, is optional on reading, and is set after construction.
    internal sealed class Shape
    {
        // The few parameters whose property has another name.
        private static readonly Dictionary<(Type, string), string> PropertyFor = new()
        {
            [(typeof(Scenario), "objectiveTerrainIds")] = nameof(Scenario.ObjectiveIds),
            [(typeof(TerrainBlock), "walkable")] = nameof(TerrainBlock.IsWalkable),
        };

        // Optional fields that are left out when null, so data without them (every match before the towers) is written
        // exactly as before. Reading already treats an init property as optional.
        private static readonly HashSet<(Type, string)> OmitWhenNull =
        [
            (typeof(TerrainBlock), nameof(TerrainBlock.Climb)),
            (typeof(TerrainBlock), nameof(TerrainBlock.Depth)),
            (typeof(CreateMatch), nameof(CreateMatch.Map)),
            (typeof(CreateMatch), nameof(CreateMatch.Roster)),
            (typeof(JoinMatch), nameof(JoinMatch.Roster)),
            (typeof(Welcome), nameof(Welcome.Warbands)),
            (typeof(MatchRecordBody), nameof(MatchRecordBody.Map)),
            (typeof(MatchRecordBody), nameof(MatchRecordBody.MapHash)),
        ];

        // Optional flags left out when false, for the same reason (every match before dual-purpose weapons).
        private static readonly HashSet<(Type, string)> OmitWhenFalse =
        [
            (typeof(ModelProfile), nameof(ModelProfile.RangedAlsoMelee)),
            (typeof(RangedAttackDeclared), nameof(RangedAttackDeclared.Elevated)),
            (typeof(FriendlyTargetChosen), nameof(FriendlyTargetChosen.Elevated)),
        ];

        // Optional numbers left out when zero, for the same reason (every match before roster stage R1's profile dice).
        private static readonly HashSet<(Type, string)> OmitWhenZero =
        [
            (typeof(ModelProfile), nameof(ModelProfile.RangedDice)),
            (typeof(ModelProfile), nameof(ModelProfile.MeleeDice)),
            (typeof(ModelProfile), nameof(ModelProfile.RangedWeaponDice)),
            (typeof(ModelProfile), nameof(ModelProfile.RangedWeaponInjuryDice)),
            (typeof(ModelProfile), nameof(ModelProfile.MeleeWeaponDice)),
            (typeof(ModelProfile), nameof(ModelProfile.MeleeWeaponInjuryDice)),
        ];

        private static readonly ConcurrentDictionary<Type, Shape> Cache = new();

        private readonly ConstructorInfo _constructor;
        private readonly List<Member> _members;

        // Constructor parameters first, then init properties (Parameter is null for those).
        private sealed record Member(string Name, byte[] Utf8Name, ParameterInfo? Parameter, PropertyInfo Property, bool NotNull)
        {
            public Type ReadType => Parameter?.ParameterType ?? Property.PropertyType;
        }

        private Shape(Type type)
        {
            Type = type;
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var widest = constructors.Length == 0 ? 0 : constructors.Max(c => c.GetParameters().Length);
            var candidates = constructors.Where(c => c.GetParameters().Length == widest).ToArray();
            if (candidates.Length != 1)
            {
                throw new NotSupportedException($"{type.Name} needs exactly one widest public constructor to travel on the wire.");
            }
            _constructor = candidates[0];
            // NullabilityInfoContext is not thread-safe, and shapes can be built on several threads at once.
            var nullability = new NullabilityInfoContext();
            _members = _constructor.GetParameters().Select(p =>
            {
                var propertyName = PropertyFor.GetValueOrDefault((type, p.Name!), p.Name!);
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? throw new NotSupportedException($"{type.Name}.{p.Name} has no public property to write.");
                var name = JsonNamingPolicy.CamelCase.ConvertName(p.Name!);
                var notNull = !p.ParameterType.IsValueType && nullability.Create(p).WriteState == NullabilityState.NotNull;
                return new Member(name, System.Text.Encoding.UTF8.GetBytes(name), p, property, notNull);
            }).ToList();
            var taken = _members.Select(m => m.Property).ToHashSet();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.SetMethod is { IsPublic: true } && !taken.Contains(p))
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                var notNull = !property.PropertyType.IsValueType && nullability.Create(property).WriteState == NullabilityState.NotNull;
                _members.Add(new Member(name, System.Text.Encoding.UTF8.GetBytes(name), null, property, notNull));
            }
            _parameterCount = _constructor.GetParameters().Length;
        }

        private readonly int _parameterCount;

        public Type Type { get; }

        public static Shape Of(Type type) => Cache.GetOrAdd(type, t => new Shape(t));

        public object ReadMembers(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var values = new object?[_members.Count];
            var seen = new bool[_members.Count];
            while (true)
            {
                if (!reader.Read())
                {
                    throw new JsonException("Unexpected end of object.");
                }
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }
                var index = -1;
                for (var i = 0; i < _members.Count; i++)
                {
                    if (reader.ValueTextEquals(_members[i].Utf8Name))
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0)
                {
                    throw new JsonException($"{Type.Name} has no such field.");
                }
                if (seen[index])
                {
                    throw new JsonException($"{Type.Name}.{_members[index].Name} is given twice.");
                }
                seen[index] = true;
                reader.Read();
                var member = _members[index];
                var value = JsonSerializer.Deserialize(ref reader, member.ReadType, options);
                if (value is null && member.NotNull)
                {
                    throw new JsonException($"{Type.Name}.{member.Name} cannot be null.");
                }
                values[index] = value;
            }

            var arguments = new object?[_parameterCount];
            for (var i = 0; i < _parameterCount; i++)
            {
                var parameter = _members[i].Parameter!;
                if (seen[i])
                {
                    arguments[i] = values[i];
                    continue;
                }
                if (!parameter.HasDefaultValue)
                {
                    throw new JsonException($"{Type.Name}.{_members[i].Name} is required.");
                }
                arguments[i] = parameter.DefaultValue ?? (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null);
            }

            var result = Construct(() => _constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, arguments, culture: null));
            for (var i = _parameterCount; i < _members.Count; i++)
            {
                if (seen[i])
                {
                    var member = _members[i];
                    Construct(() =>
                    {
                        member.Property.SetValue(result, values[i]);
                        return 0;
                    });
                }
            }
            return result;
        }

        public void WriteMembers(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            foreach (var member in _members)
            {
                var memberValue = member.Property.GetValue(value);
                if (memberValue is null && member.Parameter is null && OmitWhenNull.Contains((Type, member.Property.Name)))
                {
                    continue;
                }
                if (memberValue is false && member.Parameter is null && OmitWhenFalse.Contains((Type, member.Property.Name)))
                {
                    continue;
                }
                if (memberValue is 0 && member.Parameter is null && OmitWhenZero.Contains((Type, member.Property.Name)))
                {
                    continue;
                }
                writer.WritePropertyName(member.Utf8Name);
                JsonSerializer.Serialize(writer, memberValue, member.Property.PropertyType, options);
            }
        }
    }

    // A constructor's refusal (an argument out of range, a broken invariant) becomes an invalid-value error.
    private static TResult Construct<TResult>(Func<TResult> construct)
    {
        try
        {
            return construct();
        }
        catch (ArgumentException e)
        {
            throw new InvalidValueException(e.Message, e);
        }
        catch (InvalidOperationException e)
        {
            throw new InvalidValueException(e.Message, e);
        }
    }
}
