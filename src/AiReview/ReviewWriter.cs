using System;
using System.Collections.Generic;
using System.Linq;
using GroupDocs.Annotation;
using GroupDocs.Annotation.Models;
using GroupDocs.Annotation.Models.AnnotationModels;

namespace AiReview
{
    public sealed class ReviewResult
    {
        public int Written;
        public List<Finding> Unanchored = new List<Finding>();
    }

    /// <summary>
    /// Turns located findings into annotations and writes them into the document.
    ///
    /// Two coordinate rules decide whether the markup lands on the right words. Both are
    /// verified against GroupDocs.Annotation 26.10 and both are easy to get wrong:
    ///
    ///   * AnnotationBase.PageNumber is a 0-BASED page index, while PageInfo.PageNumber
    ///     from GetDocumentInfo() is 1-BASED. Passing the latter straight through puts every
    ///     annotation one page too far - and on the last page it is dropped without an error.
    ///
    ///   * Text markup (highlight, underline, strikeout, squiggly) takes its Points with the
    ///     origin at the BOTTOM-left of the page, while TextLineInfo.TopIndent and
    ///     AreaAnnotation.Box are measured from the TOP. A line at TopIndent t with height h
    ///     therefore spans pageHeight - t - h .. pageHeight - t.
    /// </summary>
    public static class ReviewWriter
    {
        public static ReviewResult Write(
            Annotator annotator,
            IReadOnlyList<PageLines> pages,
            IReadOnlyList<Finding> findings,
            string author)
        {
            ReviewResult result = new ReviewResult();
            User reviewer = new User { Id = 1, Name = author, Role = Role.Editor };

            foreach (Finding finding in findings)
            {
                QuoteLocation? location = QuoteLocator.Locate(pages, finding.Quote);
                if (location == null)
                {
                    result.Unanchored.Add(finding);
                    continue;
                }

                annotator.Add(BuildHighlight(location, finding, reviewer));
                result.Written++;
            }

            return result;
        }

        public static HighlightAnnotation BuildHighlight(QuoteLocation location, Finding finding, User reviewer)
        {
            double pageHeight = location.Page.Height;
            List<Point> points = new List<Point>();

            foreach (TopLeftBox box in location.Boxes)
            {
                float left = (float)box.X;
                float right = (float)(box.X + box.Width);
                float top = (float)(pageHeight - box.Y);                 // top edge, bottom-left origin
                float bottom = (float)(pageHeight - box.Y - box.Height); // bottom edge

                points.Add(new Point(left, top));
                points.Add(new Point(right, top));
                points.Add(new Point(left, bottom));
                points.Add(new Point(right, bottom));
            }

            return new HighlightAnnotation
            {
                PageNumber = location.Page.PageIndex,   // 0-based on purpose
                BackgroundColor = finding.Color(),
                Opacity = 0.5,
                Message = finding.Comment,
                CreatedOn = DateTime.Now,
                User = reviewer,
                Points = points,
                // The reply opens a thread a human can answer in Word, in Acrobat, or through
                // the API. Do not repeat the message here - it would show up twice in Word.
                Replies = new List<Reply>
                {
                    new Reply
                    {
                        Comment = $"Severity: {finding.Severity}. Reply to accept or reject this remark.",
                        RepliedOn = DateTime.Now,
                        User = reviewer
                    }
                }
            };
        }

        /// <summary>Removes every annotation written by the given author, leaving human markup untouched.</summary>
        public static int RemoveByAuthor(Annotator annotator, string author)
        {
            List<AnnotationBase> mine = annotator
                .Get()
                .Where(a => a.User != null && string.Equals(a.User.Name, author, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (mine.Count > 0)
            {
                annotator.Remove(mine);
            }

            return mine.Count;
        }
    }
}
