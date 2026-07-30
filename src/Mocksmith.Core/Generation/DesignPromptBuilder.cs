using System.Text;

namespace Mocksmith.Core.Generation;

/// <summary>Builds the shared prompt pieces both backends use.</summary>
public static class DesignPromptBuilder
{
    public const string SystemPrompt =
        """
        You are Mocksmith's design generator. You produce complete, self-contained sample web
        pages that demonstrate a design direction, ready to be reviewed, tweaked, and handed
        off to a designer or coding agent.

        OUTPUT CONTRACT (mandatory):
        1. Produce a single complete HTML5 document.
        2. Every major design decision MUST be a CSS custom property declared on :root inside the
           page's single <style> block. Always include at least: --color-bg, --color-surface,
           --color-primary, --color-accent, --color-text, --color-muted, --font-heading,
           --font-body, --space-unit, --radius. Add further tokens (type scale, shadows, borders)
           as the design needs them, and reference tokens throughout the CSS instead of literals.
        3. Include a machine-readable manifest:
           <script type="application/json" id="mocksmith-tokens">
           {"tokens":[{"name":"--color-bg","label":"Background","category":"color"}, ...]}
           </script>
           enumerating every :root token with a human-readable label and a category out of:
           color | typography | spacing | radius | shadow | other.
        4. Fully self-contained: NO external network requests of any kind. No external
           stylesheets, scripts, fonts, or images. Use system font stacks. Images only as inline
           SVG or data: URIs. Vanilla inline JavaScript only.
        5. Semantic HTML. Interactive states (hover, focus-visible, simple menus/tabs/toggles)
           via CSS and vanilla JS are encouraged.
        6. Fill the page with realistic invented content that fits the brief — never lorem ipsum.
        7. Commit to a distinctive, cohesive visual direction that fits the brief; avoid generic
           template aesthetics.
        """;

    public const string ResultInstruction =
        """
        Respond with a single JSON object and nothing else:
        {"name": "<short evocative title for this design>",
         "summary": "<1-2 sentence description>",
         "tags": ["<3-7 kebab-case keywords for style/theme/page-type>"],
         "html": "<the complete HTML document>"}
        """;

    /// <summary>JSON schema for the structured {name, summary, tags, html} return.</summary>
    public const string ResultSchemaJson =
        """
        {"type":"object","properties":{"name":{"type":"string"},"summary":{"type":"string"},"tags":{"type":"array","items":{"type":"string"}},"html":{"type":"string"}},"required":["name","summary","tags","html"],"additionalProperties":false}
        """;

    public static string BuildUserText(DesignGenerationRequest request)
    {
        var builder = new StringBuilder();
        if (request.IsRefine)
        {
            builder.AppendLine("Refine the existing sample below. Apply the instruction while preserving");
            builder.AppendLine("the overall design system and the full output contract. Return the complete");
            builder.AppendLine("rewritten document.");
            builder.AppendLine();
            builder.AppendLine($"Instruction: {request.Instruction}");
            builder.AppendLine();
            builder.AppendLine("Current sample:");
            builder.AppendLine(request.CurrentHtml);
        }
        else
        {
            builder.AppendLine("Design brief:");
            builder.AppendLine(request.Description);
            if (!string.IsNullOrWhiteSpace(request.SourceUrl))
            {
                builder.AppendLine();
                builder.AppendLine($"Inspiration source URL (fetch it for style signal if you can): {request.SourceUrl}");
            }

            if (request.Screenshots.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Reference screenshots are attached; draw palette, typography, layout, and mood from them.");
            }
        }

        if (request.ExistingTags.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Existing tag vocabulary (reuse these tags when apt instead of inventing synonyms):");
            builder.AppendLine(string.Join(", ", request.ExistingTags));
        }

        builder.AppendLine();
        builder.Append(ResultInstruction);
        return builder.ToString();
    }
}
