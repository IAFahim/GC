using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace gc.Application.Services;

public sealed class AhoCorasick
{
    private readonly int[] _gotoFunc;
    private readonly int[] _fail;
    private readonly int[] _output;
    private readonly string[] _patterns;
    private readonly int _alphabetSize;
    private readonly int _root;
    private readonly int _nodeCount;

    private readonly Dictionary<char, int> _charMap;
    private readonly char[] _reverseCharMap;

    public AhoCorasick(string[] patterns)
    {
        _patterns = patterns;

        var chars = new HashSet<char>();
        foreach (var p in patterns)
            foreach (var c in p)
                chars.Add(c);

        _charMap = new Dictionary<char, int>(chars.Count);
        _reverseCharMap = new char[chars.Count];
        var idx = 0;
        foreach (var c in chars.OrderBy(c => c))
        {
            _charMap[c] = idx;
            _reverseCharMap[idx] = c;
            idx++;
        }
        _alphabetSize = chars.Count;

        var maxNodes = patterns.Sum(p => p.Length) + 1;
        _gotoFunc = new int[maxNodes * _alphabetSize];
        Array.Fill(_gotoFunc, -1);
        _fail = new int[maxNodes];
        _output = new int[maxNodes];
        Array.Fill(_output, -1);

        _root = 0;
        _nodeCount = 1;

        for (var i = 0; i < patterns.Length; i++)
        {
            var currentState = _root;
            foreach (var c in patterns[i])
            {
                var ci = _charMap[c];
                var next = Goto(currentState, ci);
                if (next == -1)
                {
                    next = _nodeCount++;
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
                    var state = _fail[r];
                    while (Goto(state, a) == -1)
                        state = _fail[state];
                    _fail[s] = Goto(state, a);

                    if (_output[s] == -1)
                        _output[s] = _output[_fail[s]];
                }
            }
        }
    }

    public string ReplaceAll(string input, string[] replacements)
    {
        if (input.Length == 0) return input;

        var sb = new StringBuilder(input.Length);
        var i = 0;
        var len = input.Length;

        while (i < len)
        {
            var state = _root;
            var lastMatch = -1;
            var lastMatchEnd = -1;
            var j = i;

            while (j < len)
            {
                if (!_charMap.TryGetValue(input[j], out var ci))
                    break;

                var next = Goto(state, ci);
                if (next == -1)
                    break;

                state = next;
                j++;

                if (_output[state] != -1)
                {
                    lastMatch = _output[state];
                    lastMatchEnd = j;
                }
            }

            if (lastMatch != -1)
            {
                sb.Append(replacements[lastMatch]);
                i = lastMatchEnd;
            }
            else
            {
                sb.Append(input[i]);
                i++;
            }
        }

        return sb.ToString();
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
    /// Try to get the index for a character in the automaton's alphabet.
    /// Returns false if the character was never seen in any pattern.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCharIndex(char c, out int index)
    {
        return _charMap.TryGetValue(c, out index);
    }

    /// <summary>
    /// Get the next state from current state via character index.
    /// Returns -1 if no transition exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetGoto(int state, int charIndex)
    {
        return Goto(state, charIndex);
    }

    /// <summary>
    /// Get the fail function value for a state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetFail(int state)
    {
        return _fail[state];
    }

    /// <summary>
    /// Get the output index for a state (-1 means no pattern matched).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetOutput(int state)
    {
        return _output[state];
    }
}
