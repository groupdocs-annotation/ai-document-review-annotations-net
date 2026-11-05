using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GroupDocs.Annotation;
using GroupDocs.Annotation.Models.AnnotationModels;

namespace AiReview
{
    public static class Program
    {
        private const string DefaultAuthor = "AI reviewer";

        public static async Task<int> Main(string[] args)
        {
            ApplyLicense();

            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                Usage();
                return args.Length == 0 ? 1 : 0;
            }

            string command = args[0].ToLowerInvariant();
            string[] rest = args.Skip(1).ToArray();

            try
            {
                switch (command)
                {
                    case "review":
                        return Review(rest);
                    case "llm-review":
                        return await LlmReview(rest).ConfigureAwait(false);
                    case "list":
                        return List(rest);
                    case "revert":
                        return Revert(rest);
                    default:
                        Console.Error.WriteLine($"Unknown command '{command}'.");
                        Usage();
                        return 1;
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error: " + exception.Message);
                return 1;
            }
        }

        // ----------------------------------------------------------------- commands

        private static int Review(string[] args)
        {
            string document = RequirePath(args);
            string findingsPath = Option(args, "--findings") ?? Path.Combine("samples", "findings.json");
            string author = Option(args, "--author") ?? DefaultAuthor;
            string output = Option(args, "--out") ?? DefaultOutput(document, "reviewed");

            if (!File.Exists(findingsPath))
            {
                throw new FileNotFoundException($"Findings file not found: {findingsPath}");
            }

            List<Finding> findings = JsonSerializer.Deserialize<List<Finding>>(
                File.ReadAllText(findingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Finding>();

            Console.WriteLine($"{findings.Count} finding(s) read from {findingsPath}");
            return WriteReview(document, findings, author, output);
        }

        private static async Task<int> LlmReview(string[] args)
        {
            string document = RequirePath(args);
            string author = Option(args, "--author") ?? DefaultAuthor;
            string model = Option(args, "--model") ?? ClaudeReviewer.DefaultModel;
            string prompt = Option(args, "--prompt") ?? Path.Combine("prompts", "review-prompt.md");
            string output = Option(args, "--out") ?? DefaultOutput(document, "reviewed");

            string text;
            using (Annotator annotator = new Annotator(document))
            {
                List<PageLines> pages = DocumentText.Read(annotator);
                text = string.Join(
                    Environment.NewLine,
                    pages.Select(p => $"--- page {p.PageNumber} ---{Environment.NewLine}{p.PlainText()}"));
            }

            Console.WriteLine($"Asking {model} to review {Path.GetFileName(document)} ({text.Length} characters of text)...");
            List<Finding> findings = await ClaudeReviewer
                .ReviewAsync(text, model, prompt, CancellationToken.None)
                .ConfigureAwait(false);

            Console.WriteLine($"{findings.Count} finding(s) returned by the model");
            return WriteReview(document, findings, author, output);
        }

        private static int List(string[] args)
        {
            string document = RequirePath(args);

            using Annotator annotator = new Annotator(document);
            List<AnnotationBase> annotations = annotator.Get();

            if (annotations.Count == 0)
            {
                Console.WriteLine("No annotations.");
                return 0;
            }

            foreach (AnnotationBase annotation in annotations)
            {
                string author = annotation.User?.Name ?? "(no author)";
                Console.WriteLine($"#{annotation.Id} {annotation.Type} page {annotation.PageNumber + 1} by {author}");
                Console.WriteLine($"    {annotation.Message}");

                if (annotation.Replies == null)
                {
                    continue;
                }

                foreach (var reply in annotation.Replies)
                {
                    Console.WriteLine($"    reply by {reply.User?.Name ?? "(no author)"}: {reply.Comment}");
                }
            }

            return 0;
        }

        private static int Revert(string[] args)
        {
            string document = RequirePath(args);
            string author = Option(args, "--author") ?? DefaultAuthor;
            string output = Option(args, "--out") ?? DefaultOutput(document, "clean");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

            int removed;
            using (Annotator annotator = new Annotator(document))
            {
                removed = ReviewWriter.RemoveByAuthor(annotator, author);
                annotator.Save(output);
            }

            Console.WriteLine($"Removed {removed} annotation(s) by '{author}'.");
            Console.WriteLine("Saved " + output);
            return 0;
        }

        // ----------------------------------------------------------------- helpers

        private static int WriteReview(string document, List<Finding> findings, string author, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

            ReviewResult result;
            using (Annotator annotator = new Annotator(document))
            {
                List<PageLines> pages = DocumentText.Read(annotator);
                Console.WriteLine($"{pages.Count} page(s), {pages.Sum(p => p.Lines.Count)} text line(s)");

                result = ReviewWriter.Write(annotator, pages, findings, author);
                annotator.Save(output);
            }

            Console.WriteLine($"{result.Written} annotation(s) written as '{author}'.");

            foreach (Finding finding in result.Unanchored)
            {
                Console.WriteLine("  not anchored (quote not found on any page): " + Shorten(finding.Quote));
            }

            Console.WriteLine("Saved " + output);
            return result.Unanchored.Count == 0 ? 0 : 2;
        }

        private static string DefaultOutput(string document, string suffix)
        {
            string name = Path.GetFileNameWithoutExtension(document);
            string extension = Path.GetExtension(document);
            return Path.Combine("output", $"{name}.{suffix}{extension}");
        }

        private static string RequirePath(string[] args)
        {
            string? path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A document path is required.");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Document not found: " + path);
            }

            return path;
        }

        private static string? Option(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static string Shorten(string value)
        {
            value = value.Replace('\n', ' ').Trim();
            return value.Length <= 70 ? value : value.Substring(0, 67) + "...";
        }

        /// <summary>
        /// A licence is optional for trying the sample out; without one the output carries
        /// evaluation limits. Point GROUPDOCS_ANNOTATION_LICENSE at a .lic file to remove them.
        /// </summary>
        private static void ApplyLicense()
        {
            string? path = Environment.GetEnvironmentVariable("GROUPDOCS_ANNOTATION_LICENSE");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = path.Trim();
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"GROUPDOCS_ANNOTATION_LICENSE points at a missing file: {path}");
                return;
            }

            new License().SetLicense(path);
        }

        private static void Usage()
        {
            Console.WriteLine("AI review comments for GroupDocs.Annotation");
            Console.WriteLine();
            Console.WriteLine("  review     <document> [--findings findings.json] [--out file] [--author name]");
            Console.WriteLine("  llm-review <document> [--model id] [--prompt file] [--out file] [--author name]");
            Console.WriteLine("  list       <document>");
            Console.WriteLine("  revert     <document> [--author name] [--out file]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run --project src/AiReview -- review samples/service-agreement.docx");
            Console.WriteLine("  dotnet run --project src/AiReview -- list output/service-agreement.reviewed.docx");
            Console.WriteLine("  dotnet run --project src/AiReview -- revert output/service-agreement.reviewed.docx");
        }
    }
}
