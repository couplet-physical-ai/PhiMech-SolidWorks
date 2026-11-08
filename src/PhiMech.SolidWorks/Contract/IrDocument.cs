// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// POCOs for the *subset* of the PhiMech design IR this add-in writes. They mirror
// contracts/design-ir.schema.json (included in this export) and nothing more: the add-in
// hands a solid to PhiMech, it does not author enclosures. Fields the add-in never emits are
// deliberately absent so that a schema change elsewhere cannot silently change what we write.
//
// The schema has additionalProperties:false at every level, so every property here must exist
// in the schema with the same name. The seat-free smoke tests cover the output fields.

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace PhiMech.SolidWorks.Contract
{
    /// <summary>Top-level design IR document. Required by the schema: project, components.</summary>
    public sealed class IrDocument
    {
        /// <summary>Slug matching <c>^[a-z0-9][a-z0-9-]*$</c>; names PhiMech's output directory.</summary>
        [JsonProperty("project", Order = 0)]
        public string Project { get; set; }

        [JsonProperty("title", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("rev", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public string Rev { get; set; } = "A";

        [JsonProperty("company", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public string Company { get; set; } = "generic";

        /// <summary>Schema constant. Always "mm"; there is no other value and no conversion.</summary>
        [JsonProperty("units", Order = 4)]
        public string Units { get; } = "mm";

        [JsonProperty("components", Order = 5)]
        public List<IrComponent> Components { get; set; } = new List<IrComponent>();

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented) + "\n";
        }
    }

    /// <summary>One physical thing. Required by the schema: ref, source.</summary>
    public sealed class IrComponent
    {
        /// <summary>Reference designator matching <c>^[A-Za-z_][A-Za-z0-9_]*$</c>, e.g. SW1.</summary>
        [JsonProperty("ref", Order = 0)]
        public string Ref { get; set; }

        [JsonProperty("name", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("source", Order = 2)]
        public IrImportSource Source { get; set; }
    }

    /// <summary>
    /// The <c>import</c> variant of <c>$defs/source</c>: <c>{ "kind": "import", "path": ..., "scale": 1.0 }</c>.
    /// PhiMech measures the envelope from the file and treats it as a fixed box to design around.
    /// This is the only source kind the add-in ever writes.
    /// </summary>
    public sealed class IrImportSource
    {
        [JsonProperty("kind", Order = 0)]
        public string Kind { get; } = "import";

        /// <summary>Path to the STEP. Forward slashes are valid JSON and PhiMech resolves them on any OS.</summary>
        [JsonProperty("path", Order = 1)]
        public string Path { get; set; }

        /// <summary>Schema default is 1.0. Emitted explicitly because an omitted scale is easy to misread as "unknown".</summary>
        [JsonProperty("scale", Order = 2)]
        public double Scale { get; set; } = 1.0;
    }

    /// <summary>Turns a human title into a schema-valid <c>project</c> slug.</summary>
    public static class Slug
    {
        private static readonly Regex NotSlugChar = new Regex("[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex Valid = new Regex("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

        public static string From(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "untitled";
            }
            var slug = NotSlugChar.Replace(title.Trim().ToLowerInvariant(), "-").Trim('-');
            if (slug.Length == 0)
            {
                return "untitled";
            }
            return slug;
        }

        public static bool IsValid(string slug) => slug != null && Valid.IsMatch(slug);
    }
}
