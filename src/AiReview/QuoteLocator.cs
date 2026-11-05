using System;
using System.Collections.Generic;
using System.Text;

namespace AiReview
{
    /// <summary>A rectangle in page units with the origin at the TOP-left corner.</summary>
    public struct TopLeftBox
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public TopLeftBox(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>Where a quote was found: one page and one rectangle per covered line.</summary>
    public sealed class QuoteLocation
    {
        public PageLines Page = null!;
        public List<TopLeftBox> Boxes = new List<TopLeftBox>();
    }

    /// <summary>
    /// Maps a quote returned by a model onto the lines of a page.
    ///
    /// A model quotes the text it read, which is not how the document lays it out: a sentence
    /// usually wraps across two or three lines. So a quote is matched in three steps:
    ///   1. a single line that contains it,
    ///   2. a run of consecutive lines whose joined text contains it,
    ///   3. nothing - the finding is reported as unanchored instead of being placed at a guess.
    ///
    /// Horizontal offsets inside a line are estimated from the character position, which assumes
    /// an even character width. That is close enough for a highlight and never moves it off the line.
    /// </summary>
    public static class QuoteLocator
    {
        private const int MaxLinesPerQuote = 8;

        public static QuoteLocation? Locate(IReadOnlyList<PageLines> pages, string quote)
        {
            string needle = Normalize(quote);
            if (needle.Length < 3)
            {
                return null;
            }

            foreach (PageLines page in pages)
            {
                QuoteLocation? single = LocateOnSingleLine(page, needle);
                if (single != null)
                {
                    return single;
                }
            }

            foreach (PageLines page in pages)
            {
                QuoteLocation? wrapped = LocateAcrossLines(page, needle);
                if (wrapped != null)
                {
                    return wrapped;
                }
            }

            return null;
        }

        private static QuoteLocation? LocateOnSingleLine(PageLines page, string needle)
        {
            for (int i = 0; i < page.Lines.Count; i++)
            {
                string line = Normalize(page.Lines[i].Text);
                int at = line.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    continue;
                }

                return new QuoteLocation
                {
                    Page = page,
                    Boxes = new List<TopLeftBox>
                    {
                        Slice(page.Lines[i], line.Length, at, needle.Length)
                    }
                };
            }

            return null;
        }

        private static QuoteLocation? LocateAcrossLines(PageLines page, string needle)
        {
            for (int first = 0; first < page.Lines.Count; first++)
            {
                StringBuilder joined = new StringBuilder(Normalize(page.Lines[first].Text));
                int last = first;

                while (last + 1 < page.Lines.Count && last - first + 1 < MaxLinesPerQuote)
                {
                    last++;
                    joined.Append(' ').Append(Normalize(page.Lines[last].Text));

                    string window = joined.ToString();
                    int at = window.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (at < 0)
                    {
                        continue;
                    }

                    List<TopLeftBox> boxes = new List<TopLeftBox>();
                    int consumed = 0;

                    for (int i = first; i <= last; i++)
                    {
                        string lineText = Normalize(page.Lines[i].Text);
                        int lineStart = consumed;
                        int lineEnd = consumed + lineText.Length;
                        consumed = lineEnd + 1;   // the space added by the join

                        int overlapStart = Math.Max(at, lineStart);
                        int overlapEnd = Math.Min(at + needle.Length, lineEnd);
                        if (overlapEnd <= overlapStart)
                        {
                            continue;
                        }

                        boxes.Add(Slice(
                            page.Lines[i],
                            lineText.Length,
                            overlapStart - lineStart,
                            overlapEnd - overlapStart));
                    }

                    if (boxes.Count > 0)
                    {
                        return new QuoteLocation { Page = page, Boxes = boxes };
                    }
                }
            }

            return null;
        }

        private static TopLeftBox Slice(LineBox line, int lineLength, int offset, int length)
        {
            if (lineLength <= 0)
            {
                return new TopLeftBox(line.Left, line.Top, line.Width, line.Height);
            }

            double perChar = line.Width / lineLength;
            double x = line.Left + (perChar * offset);
            double width = perChar * length;

            // Never return a sliver: a very short quote still gets a visible band.
            if (width < 4)
            {
                width = Math.Min(line.Width, 4);
            }

            return new TopLeftBox(x, line.Top, width, line.Height);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            bool lastWasSpace = false;

            foreach (char c in value)
            {
                char current = c;
                switch (current)
                {
                    case '‘':
                    case '’':
                        current = '\'';
                        break;
                    case '“':
                    case '”':
                        current = '"';
                        break;
                    case '–':
                    case '—':
                        current = '-';
                        break;
                    case ' ':
                        current = ' ';
                        break;
                }

                if (char.IsWhiteSpace(current))
                {
                    if (!lastWasSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    lastWasSpace = true;
                    continue;
                }

                lastWasSpace = false;
                builder.Append(current);
            }

            return builder.ToString().TrimEnd();
        }
    }
}
