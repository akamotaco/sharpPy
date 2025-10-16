using SharpPy.PegGenerator.DataStructures;

namespace SharpPy.PegGenerator.Extractors;

/// <summary>
/// PEG 규칙(python_py.gram 등)에서 HARD/SOFT 키워드 동적 추출
/// - 'keyword' (single quote) → Hard Keywords
/// - "keyword" (double quote) → Soft Keywords
/// 하드코딩 없이 grammar 파일에서 발견된 키워드만 추출
/// </summary>
public class KeywordExtractor
{
    /// <summary>
    /// PEG 규칙에서 HARD/SOFT 키워드 추출
    /// </summary>
    public (HashSet<string> hard, HashSet<string> soft) ExtractKeywords(PegRule[] rules)
    {
        var hardKeywords = new HashSet<string>();
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

    private void ExtractFromAtom(Atom atom, HashSet<string> hard, HashSet<string> soft)
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
                        hard.Add(kw.Value);
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

            case Gather gather:
                ExtractFromAtom(gather.Separator, hard, soft);
                ExtractFromAtom(gather.Item, hard, soft);
                break;

            case Token token:
                // ASYNC and AWAIT tokens represent 'async' and 'await' keywords
                if (token.TokenType == "ASYNC")
                    hard.Add("async");
                else if (token.TokenType == "AWAIT")
                    hard.Add("await");
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
