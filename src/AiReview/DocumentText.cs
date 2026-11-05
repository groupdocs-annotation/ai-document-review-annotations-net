using System.Collections.Generic;
using System.Text;
using GroupDocs.Annotation;
using GroupDocs.Annotation.Models;

namespace AiReview
{
    /// <summary>One text line of a page, in page units with the origin at the TOP-left corner.</summary>
    public sealed class LineBox
    {
        public string Text = string.Empty;
        public double Left;
        public double Top;
        public double Width;
        public double Height;
    }

    /// <summary>One page and the lines GroupDocs.Annotation found on it.</summary>
    public sealed class PageLines
    {
        /// <summary>0-based index. This is what <c>AnnotationBase.PageNumber</c> expects.</summary>
        public int PageIndex;

        /// <summary>1-based number, exactly as <c>PageInfo.PageNumber</c> reports it.</summary>
        public int PageNumber;

        public double Width;
        public double Height;
        public List<LineBox> Lines = new List<LineBox>();

        public string PlainText()
        {
            StringBuilder builder = new StringBuilder();
            foreach (LineBox line in Lines)
            {
                builder.AppendLine(line.Text);
            }
            return builder.ToString();
        }
    }

    /// <summary>
    /// Reads the page geometry and text lines a review needs in order to anchor remarks.
    /// Works the same way for PDF, DOCX, XLSX and PPTX - the API does not change per format.
    /// </summary>
    public static class DocumentText
    {
        public static List<PageLines> Read(Annotator annotator)
        {
            List<PageLines> pages = new List<PageLines>();
            IDocumentInfo info = annotator.Document.GetDocumentInfo();

            foreach (PageInfo page in info.PagesInfo)
            {
                PageLines result = new PageLines
                {
                    // PageInfo.PageNumber is 1-based; Add() wants a 0-based index.
                    PageNumber = page.PageNumber,
                    PageIndex = page.PageNumber - 1,
                    Width = page.Width,
                    Height = page.Height
                };

                if (page.TextLines != null)
                {
                    foreach (TextLineInfo line in page.TextLines)
                    {
                        if (string.IsNullOrWhiteSpace(line.Text))
                        {
                            continue;
                        }

                        result.Lines.Add(new LineBox
                        {
                            Text = line.Text,
                            Left = line.LeftIndent,
                            Top = line.TopIndent,
                            Width = line.Width,
                            Height = line.Height
                        });
                    }
                }

                pages.Add(result);
            }

            return pages;
        }
    }
}
