using System.Text.Json.Serialization;

namespace AiReview
{
    /// <summary>
    /// One review remark produced by a model: the text it refers to and what it says about it.
    /// This is the whole contract between the model and the annotator.
    /// </summary>
    public sealed class Finding
    {
        /// <summary>Text copied verbatim from the document. Used to locate the annotation.</summary>
        [JsonPropertyName("quote")]
        public string Quote { get; set; } = string.Empty;

        /// <summary>The remark itself. Becomes the annotation message and the first reply.</summary>
        [JsonPropertyName("comment")]
        public string Comment { get; set; } = string.Empty;

        /// <summary>high | medium | low. Decides the highlight colour.</summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "medium";

        /// <summary>
        /// Highlight colour as 0xRRGGBB. PDF keeps the exact value; Word has a fixed palette of
        /// highlight colours and snaps to the nearest one, so expect a more saturated result there.
        /// </summary>
        public int Color()
        {
            switch ((Severity ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "high": return 0xFF9999;   // red
                case "low": return 0xCCE5FF;    // blue
                default: return 0xFFE699;       // amber
            }
        }
    }
}
