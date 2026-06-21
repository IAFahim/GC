using System.Runtime.CompilerServices;

namespace gc.Application.Services;

public sealed class AhoCorasick
{
    private readonly int _alphabetSize;

    private readonly int[] _asciiMap; // length 128, -1 = not in alphabet
    private readonly Dictionary<char, int> _charMap;
    private readonly int[] _fail;
    private readonly int[] _gotoFunc;
    private readonly int[] _output;
    private readonly int _root;

    /// <summary>
    ///     True when every character across all patterns is ASCII (&lt; 128).
    /// </summary>
    public bool IsAsciiOnly { get; }

    public AhoCorasick(string[] patterns)
    {
        // Filter out empty/null patterns and keep only non-empty ones
        var validPatterns = patterns.Where(p => !string.IsNullOrEmpty(p)).ToArray();

        // Handle empty pattern set - return a no-op automaton
        if (validPatterns.Length == 0)
        {
            _alphabetSize = 0;
            _gotoFunc = Array.Empty<int>();
            _fail = new[] { 0 };
            _output = new[] { -1 };
            _root = 0;
            _charMap = new Dictionary<char, int>();
            _asciiMap = new int[128];
            Array.Fill(_asciiMap, -1);
            IsAsciiOnly = true;
            return;
        }

        var chars = new HashSet<char>();
        foreach (var p in validPatterns)
            foreach (var c in p)
                chars.Add(c);

        _charMap = new Dictionary<char, int>(chars.Count);
        var idx = 0;
        foreach (var c in chars.OrderBy(c => c))
        {
            _charMap[c] = idx;
            idx++;
        }

        _alphabetSize = chars.Count;

        _asciiMap = new int[128];
        Array.Fill(_asciiMap, -1);
        var asciiOnly = true;
        foreach (var kvp in _charMap)
            if (kvp.Key < 128) _asciiMap[kvp.Key] = kvp.Value;
            else asciiOnly = false;
        IsAsciiOnly = asciiOnly;

        var maxNodes = validPatterns.Sum(p => p.Length) + 1;

        // Dense transition table is O(nodes x alphabet). Compute in long to avoid
        // silent Int32 overflow on large pattern sets / large Unicode alphabets,
        // and reject sizes that would otherwise overflow or exhaust memory.
        const long MaxTransitionTableEntries = 256L * 1024 * 1024; // 256M ints = 1 GiB cap
        var tableSize = (long)maxNodes * _alphabetSize;
        if (tableSize > MaxTransitionTableEntries)
            throw new ArgumentException(
                $"Aho-Corasick transition table too large: {maxNodes} nodes x {_alphabetSize} distinct chars " +
                $"= {tableSize} entries (limit {MaxTransitionTableEntries}). " +
                "Reduce the number/length of content patterns or the distinct-character set.",
                nameof(patterns));

        _gotoFunc = new int[(int)tableSize];
        Array.Fill(_gotoFunc, -1);
        _fail = new int[maxNodes];
        _output = new int[maxNodes];
        Array.Fill(_output, -1);

        _root = 0;
        var nodeCount = 1;

        for (var i = 0; i < validPatterns.Length; i++)
        {
            var currentState = _root;
            foreach (var c in validPatterns[i])
            {
                var ci = _charMap[c];
                var next = Goto(currentState, ci);
                if (next == -1)
                {
                    next = nodeCount++;
                    SetGoto(currentState, ci, next);
                }

                currentState = next;
            }

            _output[currentState] = i;
        }

        var queue = new Queue<int>();

        for (var a = 0; a < _alphabetSize; a++)
        {
            var s = Goto(_root, a);
            if (s != -1 && s != _root)
            {
                _fail[s] = _root;
                queue.Enqueue(s);
            }
            else if (s == -1)
            {
                SetGoto(_root, a, _root);
            }
        }

        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            for (var a = 0; a < _alphabetSize; a++)
            {
                var s = Goto(r, a);
                if (s != -1 && s != _root)
                {
                    queue.Enqueue(s);
                    // _fail[r]'s row is already dense (processed earlier in BFS; the root
                    // row was densified above), so this resolves in a single lookup.
                    _fail[s] = Goto(_fail[r], a);

                    if (_output[s] == -1)
                        _output[s] = _output[_fail[s]];
                }
                else if (s == -1)
                {
                    // Complete the delta (goto) automaton: copy the transition from the
                    // fail state so every (state, char) pair resolves in one array read,
                    // turning the per-character scan into a single lookup with no fail walk.
                    SetGoto(r, a, Goto(_fail[r], a));
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Goto(int state, int c)
    {
        return _gotoFunc[state * _alphabetSize + c];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetGoto(int state, int c, int value)
    {
        _gotoFunc[state * _alphabetSize + c] = value;
    }

    /// <summary>
    ///     Try to get the index for a character in the automaton's alphabet.
    ///     Returns false if the character was never seen in any pattern.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCharIndex(char c, out int index)
    {
        if (c < 128)
        {
            index = _asciiMap[c];
            return index >= 0;
        }

        return _charMap.TryGetValue(c, out index);
    }

    /// <summary>
    ///     Get the next state from the current state via character index. The transition
    ///     table is a complete delta (goto) automaton, so this is a single array lookup that
    ///     always returns a valid state — no fail-link walk is required by callers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Next(int state, int charIndex)
    {
        return _gotoFunc[state * _alphabetSize + charIndex];
    }

    /// <summary>
    ///     Get the next state from current state via character index.
    ///     With the delta automaton this never returns -1 (transitions are complete).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetGoto(int state, int charIndex)
    {
        return Goto(state, charIndex);
    }

    /// <summary>
    ///     Get the fail function value for a state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFail(int state)
    {
        return _fail[state];
    }

    /// <summary>
    ///     Get the output index for a state (-1 means no pattern matched).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetOutput(int state)
    {
        return _output[state];
    }
}