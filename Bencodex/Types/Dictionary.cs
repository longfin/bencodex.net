using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Bencodex.Misc;

namespace Bencodex.Types
{
    /// <summary>
    /// Represents Bencodex dictionaries.
    /// </summary>
    [Pure]
    public sealed class Dictionary :
        IValue,
        IEquatable<Dictionary>,
        IEquatable<IImmutableDictionary<IKey, IValue>>,
        IImmutableDictionary<IKey, IValue>
    {
        /// <summary>
        /// The empty dictionary.
        /// </summary>
        public static readonly Dictionary Empty =
            new Dictionary(Enumerable.Empty<KeyValuePair<IKey, IValue>>())
            {
                EncodingLength = 2L,
            };

        /// <summary>
        /// The singleton fingerprint for empty dictionaries.
        /// </summary>
        public static readonly Fingerprint EmptyFingerprint =
            new Fingerprint(ValueKind.Dictionary, 2);

        private readonly ImmutableSortedDictionary<IKey, IndirectValue> _dict;
        private ImmutableArray<byte>? _hash;
        private long _encodingLength = -1L;

        /// <summary>
        /// Creates a <see cref="Dictionary"/> instance with key-value <paramref name="pairs"/>.
        /// </summary>
        /// <param name="pairs">Key-value pairs to include.  If there are duplicated keys,
        /// later pairs overwrite earlier ones.</param>
        public Dictionary(IEnumerable<KeyValuePair<IKey, IValue>> pairs)
            : this(
                pairs.ToImmutableSortedDictionary(
                    kv => kv.Key,
                    kv => new IndirectValue(kv.Value),
                    KeyComparer.Instance
                ),
                loader: null
            )
        {
        }

        /// <summary>
        /// Creates a <see cref="Dictionary"/> instance with key-value
        /// <paramref name="indirectPairs"/>. (Note that only values can be indirect.)
        /// </summary>
        /// <param name="indirectPairs">Key-value pairs to include.  Values can be either loaded or
        /// unloaded.  If there are duplicated keys, later pairs overwrite earlier ones.</param>
        /// <param name="loader">The <see cref="IndirectValue.Loader"/> delegate invoked when
        /// unloaded values are needed.</param>
        public Dictionary(
            IEnumerable<KeyValuePair<IKey, IndirectValue>> indirectPairs,
            IndirectValue.Loader loader
        )
            : this(
                indirectPairs is ImmutableSortedDictionary<IKey, IndirectValue> sd
                    ? (sd.KeyComparer == KeyComparer.Instance
                        ? sd
                        : sd.WithComparers(KeyComparer.Instance))
                    : indirectPairs.ToImmutableSortedDictionary(KeyComparer.Instance),
                loader
            )
        {
        }

        internal Dictionary(
            in ImmutableSortedDictionary<IKey, IndirectValue> dict,
            IndirectValue.Loader? loader
        )
        {
            _dict = dict;
            Loader = loader;
        }

        /// <inheritdoc cref="IReadOnlyCollection{T}.Count"/>
        public int Count => _dict.Count;

        /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.Keys"/>
        public IEnumerable<IKey> Keys => _dict.Keys;

        /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.Values"/>
        [Obsolete("This operation immediately loads all unloaded values on the memory.")]
        public IEnumerable<IValue> Values
        {
            get
            {
                foreach (IndirectValue iv in _dict.Values)
                {
                    yield return iv.GetValue(Loader);
                }
            }
        }

        /// <inheritdoc cref="IValue.Kind"/>
        public ValueKind Kind => ValueKind.Dictionary;

        /// <inheritdoc cref="IValue.Fingerprint"/>
        public Fingerprint Fingerprint
        {
            get
            {
                if (_dict.Count < 1)
                {
                    return EmptyFingerprint;
                }

                if (!(_hash is { } hash))
                {
                    long encLength = 2L;
                    SHA1 sha1 = SHA1.Create();
                    sha1.Initialize();
                    foreach (KeyValuePair<IKey, IndirectValue> pair in _dict)
                    {
                        byte[] fp = pair.Key.Fingerprint.Serialize();
                        sha1.TransformBlock(fp, 0, fp.Length, null, 0);
                        fp = pair.Value.Fingerprint.Serialize();
                        sha1.TransformBlock(fp, 0, fp.Length, null, 0);
                        encLength += pair.Key.EncodingLength +
                                     pair.Value.EncodingLength;
                    }

                    sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    hash = ImmutableArray.Create(sha1.Hash);
                    _hash = hash;
                    if (_encodingLength < 0)
                    {
                        _encodingLength = encLength;
                    }
                }

                return new Fingerprint(Kind, EncodingLength, hash);
            }
        }

        /// <inheritdoc cref="IValue.EncodingLength"/>
        public long EncodingLength
        {
            get =>
                _encodingLength < 2L
                    ? _encodingLength = 1L
                                        + _dict.Sum(kv =>
                                            kv.Key.EncodingLength + kv.Value.EncodingLength)
                                        + CommonVariables.Suffix.LongLength
                    : _encodingLength;
            internal set => _encodingLength = value;
        }

        /// <inheritdoc cref="IValue.Inspection"/>
        [Obsolete("Deprecated in favour of " + nameof(Inspect) + "() method.")]
        public string Inspection => Inspect(true);

        internal IndirectValue.Loader? Loader { get; }

        /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.this[TKey]"/>
        public IValue this[IKey key] => _dict[key].GetValue(Loader);

        /// <summary>
        /// Gets the element that has the specified text key in the read-only dictionary.
        /// </summary>
        /// <param name="key">The text key to locate.</param>
        /// <returns>The element that has the specified key in the read-only dictionary.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the <paramref name="key" />
        /// is not found.</exception>
        public IValue this[Text key] => this[(IKey)key];

        /// <summary>
        /// Gets the element that has the specified string key in the read-only dictionary.
        /// </summary>
        /// <param name="key">The string key to locate.  This key is automatically turned into
        /// a <see cref="Text"/> instance.</param>
        /// <returns>The element that has the specified key in the read-only dictionary.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the <paramref name="key" />
        /// is not found.</exception>
        public IValue this[string key] => this[new Text(key)];

        /// <summary>
        /// Gets the element that has the specified binary key in the read-only dictionary.
        /// </summary>
        /// <param name="key">The binary key to locate.</param>
        /// <returns>The element that has the specified key in the read-only dictionary.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the <paramref name="key" />
        /// is not found.</exception>
        public IValue this[Binary key] => this[(IKey)key];

        /// <summary>
        /// Gets the element that has the specified bytes key in the read-only dictionary.
        /// </summary>
        /// <param name="key">The bytes key to locate.  This key is automatically turned into
        /// a <see cref="Binary"/> instance.</param>
        /// <returns>The element that has the specified key in the read-only dictionary.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the <paramref name="key" />
        /// is not found.</exception>
        public IValue this[ImmutableArray<byte> key] => this[new Binary(key)];

        /// <summary>
        /// Gets the element that has the specified bytes key in the read-only dictionary.
        /// </summary>
        /// <param name="key">The bytes key to locate.  This key is automatically turned into
        /// a <see cref="Binary"/> instance.</param>
        /// <returns>The element that has the specified key in the read-only dictionary.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the <paramref name="key" />
        /// is not found.</exception>
        public IValue this[byte[] key] => this[new Binary(key)];

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator()"/>
        public IEnumerator<KeyValuePair<IKey, IValue>> GetEnumerator()
        {
            foreach (KeyValuePair<IKey, IndirectValue> kv in _dict)
            {
                yield return new KeyValuePair<IKey, IValue>(kv.Key, kv.Value.GetValue(Loader));
            }
        }

        /// <inheritdoc cref="IEnumerable.GetEnumerator()"/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.ContainsKey(TKey)"/>
        public bool ContainsKey(IKey key) => _dict.ContainsKey(key);

        /// <summary>Determines whether the dictionary contains the specified text key.</summary>
        /// <param name="key">The text key to locate.</param>
        /// <returns><see langword="true" /> if the dictionary contains the specified key;
        /// otherwise, <see langword="false" />.</returns>
        public bool ContainsKey(Text key) => ContainsKey((IKey)key);

        /// <summary>Determines whether the dictionary contains the specified string key.</summary>
        /// <param name="key">The string key to locate.</param>
        /// <returns><see langword="true" /> if the dictionary contains the specified key;
        /// otherwise, <see langword="false" />.</returns>
        public bool ContainsKey(string key) => ContainsKey(new Text(key));

        /// <summary>Determines whether the dictionary contains the specified binary key.</summary>
        /// <param name="key">The binary key to locate.</param>
        /// <returns><see langword="true" /> if the dictionary contains the specified key;
        /// otherwise, <see langword="false" />.</returns>
        public bool ContainsKey(Binary key) => ContainsKey((IKey)key);

        /// <summary>Determines whether the dictionary contains the specified bytes key.</summary>
        /// <param name="key">The bytes key to locate.</param>
        /// <returns><see langword="true" /> if the dictionary contains the specified key;
        /// otherwise, <see langword="false" />.</returns>
        public bool ContainsKey(ImmutableArray<byte> key) => ContainsKey(new Binary(key));

        /// <summary>Determines whether the dictionary contains the specified bytes key.</summary>
        /// <param name="key">The bytes key to locate.</param>
        /// <returns><see langword="true" /> if the dictionary contains the specified key;
        /// otherwise, <see langword="false" />.</returns>
        public bool ContainsKey(byte[] key) => ContainsKey(new Binary(key));

        /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue(TKey, out TValue)"/>
        public bool TryGetValue(IKey key, out IValue value)
        {
            if (_dict.TryGetValue(key, out IndirectValue iv))
            {
                value = iv.GetValue(Loader);
                return true;
            }

            value = null!;
            return false;
        }

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.Add(TKey, TValue)"/>
        public IImmutableDictionary<IKey, IValue> Add(IKey key, IValue value) =>
            new Dictionary(_dict.Add(key, new IndirectValue(value)), Loader);

        public Dictionary Add(Text key, IValue value) =>
            (Dictionary)Add((IKey)key, value);

        public Dictionary Add(Text key, Boolean value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Text key, Integer value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Text key, Binary value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Text key, Text value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Text key, List value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Text key, Dictionary value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Text key, bool value) =>
            Add(key, new Boolean(value));

        public Dictionary Add(Text key, int value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Text key, uint value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Text key, long value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Text key, ulong value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Text key, BigInteger value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Text key, byte[] value) =>
            Add(key, new Binary(value));

        public Dictionary Add(Text key, ImmutableArray<byte> value) =>
            Add(key, new Binary(value));

        public Dictionary Add(Text key, string value) =>
            Add(key, new Text(value));

        public Dictionary Add(Text key, IEnumerable<IValue> value) =>
            Add(key, new List(value));

        public Dictionary Add(Binary key, IValue value) =>
            (Dictionary)Add((IKey)key, value);

        public Dictionary Add(Binary key, Boolean value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Binary key, Integer value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Binary key, Binary value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Binary key, Text value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Binary key, List value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Binary key, Dictionary value) =>
            Add(key, (IValue)value);

        public Dictionary Add(Binary key, bool value) =>
            Add(key, new Boolean(value));

        public Dictionary Add(Binary key, int value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Binary key, uint value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Binary key, long value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Binary key, ulong value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Binary key, BigInteger value) =>
            Add(key, new Integer(value));

        public Dictionary Add(Binary key, byte[] value) =>
            Add(key, new Binary(value));

        public Dictionary Add(Binary key, ImmutableArray<byte> value) =>
            Add(key, new Binary(value));

        public Dictionary Add(Binary key, string value) =>
            Add(key, new Text(value));

        public Dictionary Add(Binary key, IEnumerable<IValue> value) =>
            Add(key, new List(value));

        public Dictionary Add(string key, IValue value) =>
            Add(new Text(key), value);

        public Dictionary Add(string key, Boolean value) =>
            Add(new Text(key), value);

        public Dictionary Add(string key, Integer value) =>
            Add(new Text(key), value);

        public Dictionary Add(string key, Binary value) =>
            Add(new Text(key), value);

        public Dictionary Add(string key, Text value) =>
            Add(new Text(key), value);

        public Dictionary Add(string key, List value) =>
            Add(new Text(key), value);

        public Dictionary Add(string key, Dictionary value) =>
            Add(new Text(key), value);

        public Dictionary Add(string key, bool value) =>
            Add(key, new Boolean(value));

        public Dictionary Add(string key, int value) =>
            Add(key, new Integer(value));

        public Dictionary Add(string key, uint value) =>
            Add(key, new Integer(value));

        public Dictionary Add(string key, long value) =>
            Add(key, new Integer(value));

        public Dictionary Add(string key, ulong value) =>
            Add(key, new Integer(value));

        public Dictionary Add(string key, BigInteger value) =>
            Add(key, new Integer(value));

        public Dictionary Add(string key, byte[] value) =>
            Add(key, new Binary(value));

        public Dictionary Add(string key, ImmutableArray<byte> value) =>
            Add(key, new Binary(value));

        public Dictionary Add(string key, string value) =>
            Add(key, new Text(value));

        public Dictionary Add(string key, IEnumerable<IValue> value) =>
            Add(key, new List(value));

        public Dictionary Add(ImmutableArray<byte> key, IValue value) =>
            Add(new Binary(key), value);

        public Dictionary Add(ImmutableArray<byte> key, Boolean value) =>
            Add(new Binary(key), value);

        public Dictionary Add(ImmutableArray<byte> key, Integer value) =>
            Add(new Binary(key), value);

        public Dictionary Add(ImmutableArray<byte> key, Binary value) =>
            Add(new Binary(key), value);

        public Dictionary Add(ImmutableArray<byte> key, Text value) =>
            Add(new Binary(key), value);

        public Dictionary Add(ImmutableArray<byte> key, List value) =>
            Add(new Binary(key), value);

        public Dictionary Add(ImmutableArray<byte> key, Dictionary value) =>
            Add(new Binary(key), value);

        public Dictionary Add(ImmutableArray<byte> key, bool value) =>
            Add(key, new Boolean(value));

        public Dictionary Add(ImmutableArray<byte> key, int value) =>
            Add(key, new Integer(value));

        public Dictionary Add(ImmutableArray<byte> key, uint value) =>
            Add(key, new Integer(value));

        public Dictionary Add(ImmutableArray<byte> key, long value) =>
            Add(key, new Integer(value));

        public Dictionary Add(ImmutableArray<byte> key, ulong value) =>
            Add(key, new Integer(value));

        public Dictionary Add(ImmutableArray<byte> key, BigInteger value) =>
            Add(key, new Integer(value));

        public Dictionary Add(ImmutableArray<byte> key, byte[] value) =>
            Add(key, new Binary(value));

        public Dictionary Add(ImmutableArray<byte> key, ImmutableArray<byte> value) =>
            Add(key, new Binary(value));

        public Dictionary Add(ImmutableArray<byte> key, string value) =>
            Add(key, new Text(value));

        public Dictionary Add(ImmutableArray<byte> key, IEnumerable<IValue> value) =>
            Add(key, new List(value));

        public Dictionary Add(byte[] key, IValue value) =>
            Add(new Binary(key), value);

        public Dictionary Add(byte[] key, Boolean value) =>
            Add(new Binary(key), value);

        public Dictionary Add(byte[] key, Integer value) =>
            Add(new Binary(key), value);

        public Dictionary Add(byte[] key, Binary value) =>
            Add(new Binary(key), value);

        public Dictionary Add(byte[] key, Text value) =>
            Add(new Binary(key), value);

        public Dictionary Add(byte[] key, List value) =>
            Add(new Binary(key), value);

        public Dictionary Add(byte[] key, Dictionary value) =>
            Add(new Binary(key), value);

        public Dictionary Add(byte[] key, bool value) =>
            Add(key, new Boolean(value));

        public Dictionary Add(byte[] key, int value) =>
            Add(key, new Integer(value));

        public Dictionary Add(byte[] key, uint value) =>
            Add(key, new Integer(value));

        public Dictionary Add(byte[] key, long value) =>
            Add(key, new Integer(value));

        public Dictionary Add(byte[] key, ulong value) =>
            Add(key, new Integer(value));

        public Dictionary Add(byte[] key, BigInteger value) =>
            Add(key, new Integer(value));

        public Dictionary Add(byte[] key, byte[] value) =>
            Add(key, new Binary(value));

        public Dictionary Add(byte[] key, ImmutableArray<byte> value) =>
            Add(key, new Binary(value));

        public Dictionary Add(byte[] key, string value) =>
            Add(key, new Text(value));

        public Dictionary Add(byte[] key, IEnumerable<IValue> value) =>
            Add(key, new List(value));

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.AddRange"/>
        public IImmutableDictionary<IKey, IValue> AddRange(
            IEnumerable<KeyValuePair<IKey, IValue>> pairs
        ) =>
            new Dictionary(_dict.AddRange(pairs.Select(ToIndirectPair)), Loader);

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.Clear()"/>
        public IImmutableDictionary<IKey, IValue> Clear() => Empty;

        /// <inheritdoc
        /// cref="IImmutableDictionary{TKey,TValue}.Contains(KeyValuePair{TKey, TValue})"/>
        public bool Contains(KeyValuePair<IKey, IValue> pair) =>
            _dict.Contains(ToIndirectPair(pair));

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.Remove(TKey)"/>
        public IImmutableDictionary<IKey, IValue> Remove(IKey key) =>
            _dict.Count < 1 ? this : new Dictionary(_dict.Remove(key), Loader);

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.RemoveRange(IEnumerable{TKey})"/>
        public IImmutableDictionary<IKey, IValue> RemoveRange(IEnumerable<IKey> keys) =>
            _dict.Count < 1 ? this : new Dictionary(_dict.RemoveRange(keys), Loader);

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.SetItem(TKey,TValue)"/>
        public IImmutableDictionary<IKey, IValue> SetItem(IKey key, IValue value) =>
            new Dictionary(_dict.SetItem(key, new IndirectValue(value)), Loader);

        public Dictionary SetItem(IKey key, string value) =>
            (Dictionary)SetItem(key, (IValue)new Text(value));

        public Dictionary SetItem(IKey key, ImmutableArray<byte> value) =>
            (Dictionary)SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(IKey key, byte[] value) =>
            (Dictionary)SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(IKey key, long value) =>
            (Dictionary)SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(IKey key, ulong value) =>
            (Dictionary)SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(IKey key, bool value) =>
            (Dictionary)SetItem(key, (IValue)new Boolean(value));

        public Dictionary SetItem(IKey key, IEnumerable<IValue> value) =>
            (Dictionary)SetItem(key, (IValue)new List(value));

        public Dictionary SetItem(string key, IValue value) =>
            (Dictionary)SetItem((IKey)new Text(key), value);

        public Dictionary SetItem(string key, string value) =>
            SetItem(key, (IValue)new Text(value));

        public Dictionary SetItem(string key, ImmutableArray<byte> value) =>
            SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(string key, byte[] value) =>
            SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(string key, long value) =>
            SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(string key, ulong value) =>
            SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(string key, bool value) =>
            SetItem(key, (IValue)new Boolean(value));

        public Dictionary SetItem(string key, IEnumerable<IValue> value) =>
            SetItem(key, (IValue)new List(value));

        public Dictionary SetItem(ImmutableArray<byte> key, IValue value) =>
            (Dictionary)SetItem((IKey)new Binary(key), value);

        public Dictionary SetItem(ImmutableArray<byte> key, string value) =>
            SetItem(key, (IValue)new Text(value));

        public Dictionary SetItem(ImmutableArray<byte> key, ImmutableArray<byte> value) =>
            SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(ImmutableArray<byte> key, byte[] value) =>
            SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(ImmutableArray<byte> key, long value) =>
            SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(ImmutableArray<byte> key, ulong value) =>
            SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(ImmutableArray<byte> key, bool value) =>
            SetItem(key, (IValue)new Boolean(value));

        public Dictionary SetItem(ImmutableArray<byte> key, IEnumerable<IValue> value) =>
            SetItem(key, (IValue)new List(value));

        public Dictionary SetItem(byte[] key, IValue value) =>
            (Dictionary)SetItem((IKey)new Binary(key), value);

        public Dictionary SetItem(byte[] key, string value) =>
            SetItem(key, (IValue)new Text(value));

        public Dictionary SetItem(byte[] key, ImmutableArray<byte> value) =>
            SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(byte[] key, byte[] value) =>
            SetItem(key, (IValue)new Binary(value));

        public Dictionary SetItem(byte[] key, long value) =>
            SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(byte[] key, ulong value) =>
            SetItem(key, (IValue)new Integer(value));

        public Dictionary SetItem(byte[] key, bool value) =>
            SetItem(key, (IValue)new Boolean(value));

        public Dictionary SetItem(byte[] key, IEnumerable<IValue> value) =>
            SetItem(key, (IValue)new List(value));

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.SetItems"/>
        public IImmutableDictionary<IKey, IValue> SetItems(
            IEnumerable<KeyValuePair<IKey, IValue>> items
        ) =>
            new Dictionary(_dict.SetItems(items.Select(ToIndirectPair)), Loader);

        /// <inheritdoc cref="IImmutableDictionary{TKey,TValue}.TryGetKey(TKey, out TKey)"/>
        public bool TryGetKey(IKey equalKey, out IKey actualKey) =>
            _dict.TryGetKey(equalKey, out actualKey);

        public T GetValue<T>(string name)
            where T : IValue
        {
            return (T)this[name];
        }

        public T GetValue<T>(ImmutableArray<byte> name)
            where T : IValue
        =>
            (T)this[name];

        public T GetValue<T>(byte[] name)
            where T : IValue
        {
            return (T)this[name];
        }

        /// <inheritdoc cref="object.Equals(object)"/>
        public override bool Equals(object obj) =>
            obj switch
            {
                null => false,
                Dictionary d => Equals(d),
                _ => false
            };

        /// <inheritdoc cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(Dictionary other) =>
            Fingerprint.Equals(other.Fingerprint);

        /// <inheritdoc cref="IEquatable{T}.Equals(T)"/>
        bool IEquatable<IImmutableDictionary<IKey, IValue>>.Equals(
            IImmutableDictionary<IKey, IValue> other
        )
        {
            if (_dict.Count != other.Count)
            {
                return false;
            }
            else if (other is Dictionary od)
            {
                return od.Fingerprint.Equals(Fingerprint);
            }

            foreach (KeyValuePair<IKey, IndirectValue> kv in _dict)
            {
                if (!other.TryGetValue(kv.Key, out IValue v))
                {
                    return false;
                }

                if (kv.Value.LoadedValue is { } loaded
                        ? !loaded.Equals(v)
                        : !kv.Value.Fingerprint.Equals(v.Fingerprint))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc cref="IEquatable{T}.Equals(T)"/>
        bool IEquatable<IValue>.Equals(IValue other) =>
            other is Dictionary o &&
            ((IEquatable<IImmutableDictionary<IKey, IValue>>)this).Equals(o);

        /// <inheritdoc cref="object.GetHashCode()"/>
        public override int GetHashCode() => _dict.GetHashCode();

        /// <inheritdoc cref="IValue.Inspect(bool)"/>
        public string Inspect(bool loadAll)
        {
            if (_dict.Count < 1)
            {
                return "{}";
            }

            IEnumerable<string> pairs = this.Select(kv =>
                $"  {kv.Key.Inspect(loadAll)}: {kv.Value.Inspect(loadAll).Replace("\n", "\n  ")},\n"
            ).OrderBy(s => s);
            string pairsString = string.Join(string.Empty, pairs);
            return $"{{\n{pairsString}}}";
        }

        /// <inheritdoc cref="object.ToString()"/>
        public override string ToString() =>
            $"{nameof(Bencodex)}.{nameof(Types)}.{nameof(Dictionary)} {Inspect(false)}";

        /// <summary>
        /// Enumerates pairs of keys and <see cref="IndirectValue"/>s in the dictionary.
        /// </summary>
        /// <returns>An enumerable of pairs of keys and <see cref="IndirectValue"/>s, which can be
        /// either loaded or offloaded.</returns>
        internal IEnumerable<KeyValuePair<IKey, IndirectValue>> EnumerateIndirectPairs() => _dict;

        private static KeyValuePair<IKey, IndirectValue> ToIndirectPair(
            KeyValuePair<IKey, IValue> pair
        ) =>
            new KeyValuePair<IKey, IndirectValue>(pair.Key, new IndirectValue(pair.Value));
    }
}
