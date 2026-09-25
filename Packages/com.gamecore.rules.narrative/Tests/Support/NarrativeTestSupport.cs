#nullable enable
namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// Expectations the narrative rules tests share. The chapters are read from the rules package rather than
    /// re-literalized, so a test never hard-codes content the declarative source owns (P-015).
    /// </summary>
    internal static class NarrativeTestSupport
    {
        /// <summary>Chapter One as the rules declare it.</summary>
        internal static ChapterDefinition ChapterOne => NarrativeChapters.Get(NarrativeChapters.ChapterOneTag);

        /// <summary>Chapter Two as the rules declare it.</summary>
        internal static ChapterDefinition ChapterTwo => NarrativeChapters.Get(NarrativeChapters.ChapterTwoTag);

        /// <summary>True when the text is exactly one canonical 64-character lowercase SHA-256 digest (P-004).</summary>
        internal static bool IsLowercaseHex64(string text)
        {
            if (text == null || text.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
