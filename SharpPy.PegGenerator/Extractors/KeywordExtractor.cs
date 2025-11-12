using SharpPy.PegGenerator.DataStructures;
using SharpPy.Generated;

namespace SharpPy.PegGenerator.Extractors;

/// <summary>
/// PEG 규칙(python_py.gram 등)에서 HARD/SOFT 키워드 동적 추출
/// - 'keyword' (single quote) → Hard Keywords (토큰 번호 할당)
/// - "keyword" (double quote) → Soft Keywords
/// CPython 3.12 방식: 각 hard keyword에 500부터 시작하는 유니크 번호 할당
/// </summary>
public class KeywordExtractor
{
    private int _keywordCounter = 499;  // CPython 방식: 500부터 시작

    /// <summary>
    /// PEG 규칙에서 HARD/SOFT 키워드 추출
    /// Hard keywords: Dictionary[keyword, tokenNumber]
    /// Soft keywords: HashSet[keyword]
    /// </summary>
    public (Dictionary<string, int> hard, HashSet<string> soft) ExtractKeywords(PegRule[] rules)
    {
        var hardKeywords = new Dictionary<string, int>();
        var softKeywords = new HashSet<string>();

        foreach (var rule in rules)
        {
            foreach (var alt in rule.Alternatives)
            {
                foreach (var item in alt.Items)
                {
                    ExtractFromAtom(item.Atom, hardKeywords, softKeywords);
                }
            }
        }

        return (hardKeywords, softKeywords);
    }

    private void ExtractFromAtom(Atom atom, Dictionary<string, int> hard, HashSet<string> soft)
    {
        switch (atom)
        {
            case Keyword kw:
                // Only extract alphabetic keywords (not operators like ',', ';', '!=', etc.)
                if (IsAlphabeticKeyword(kw.Value))
                {
                    if (kw.IsSoft)
                        soft.Add(kw.Value);
                    else
                    {
                        // CPython 방식: 각 hard keyword에 유니크 번호 할당
                        if (!hard.ContainsKey(kw.Value))
                        {
                            _keywordCounter++;
                            hard[kw.Value] = _keywordCounter;
                        }
                    }
                }
                break;

            case Group g:
                foreach (var alt in g.Alternatives)
                    foreach (var item in alt.Items)
                        ExtractFromAtom(item.Atom, hard, soft);
                break;

            case Optional opt:
                ExtractFromAtom(opt.Inner, hard, soft);
                break;

            case ZeroOrMore zm:
                ExtractFromAtom(zm.Inner, hard, soft);
                break;

            case OneOrMore om:
                ExtractFromAtom(om.Inner, hard, soft);
                break;

            case PositiveLookahead pl:
                ExtractFromAtom(pl.Inner, hard, soft);
                break;

            case NegativeLookahead nl:
                ExtractFromAtom(nl.Inner, hard, soft);
                break;

            case Forced forced:
                ExtractFromAtom(forced.Inner, hard, soft);
                break;

            case Gather gather:
                ExtractFromAtom(gather.Separator, hard, soft);
                ExtractFromAtom(gather.Item, hard, soft);
                break;

            case Token token:
                // ASYNC and AWAIT tokens represent 'async' and 'await' keywords
                // These are special context-sensitive keywords with fixed token numbers from Grammar/Tokens
                // Use PyToken.GetTypeValue to get actual enum values from generated PyTokens.cs
                if (token.TokenType == "ASYNC")
                {
                    if (!hard.ContainsKey("async"))
                    {
                        hard["async"] = PyToken.GetTypeValue("ASYNC");
                    }
                }
                else if (token.TokenType == "AWAIT")
                {
                    if (!hard.ContainsKey("await"))
                    {
                        hard["await"] = PyToken.GetTypeValue("AWAIT");
                    }
                }
                break;

            case RuleRef:
                // RuleRef는 키워드가 아님
                break;

            default:
                throw new Exception($"Unknown atom type: {atom.GetType().Name}");
        }
    }

    private bool IsAlphabeticKeyword(string value)
    {
        // Keywords must start with a letter and contain only letters/underscores
        // This filters out operators like ',', ';', '!=', '...', etc.
        if (string.IsNullOrEmpty(value))
            return false;

        if (!char.IsLetter(value[0]) && value[0] != '_')
            return false;

        foreach (char c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }
}
