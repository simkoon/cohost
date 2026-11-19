using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using CoHost.Contracts;

// Writes one JSON Schema per contract root. The TypeScript admin types are generated from these files,
// so the schemas are produced with the exact serializer options used on the wire (ContractJson.Options).
if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("usage: SchemaGen <output-dir>");
    return 2;
}

var outputDir = args[0];
Directory.CreateDirectory(outputDir);

var exporterOptions = new JsonSchemaExporterOptions
{
    TreatNullObliviousAsNonNullable = true,
    TransformSchemaNode = static (context, node) =>
    {
        // Named interfaces in the generated TypeScript need a title; enums and BCL types stay anonymous.
        if (node is JsonObject obj
            && context.TypeInfo.Type is { IsEnum: false, IsPrimitive: false } type
            && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true
            && !obj.ContainsKey("title"))
        {
            obj["title"] = type.Name;
        }

        return node;
    },
};

var writeOptions = new JsonSerializerOptions { WriteIndented = true };

var roots = new (string Name, Type Type)[]
{
    ("AgentMessage", typeof(AgentMessage)),
    ("ClientMessage", typeof(ClientMessage)),
    ("AdminMessage", typeof(AdminMessage)),
    ("ServerInfo", typeof(ServerInfo)),
};

foreach (var (name, type) in roots)
{
    var schema = JsonSchemaExporter.GetJsonSchemaAsNode(ContractJson.Options, type, exporterOptions).AsObject();

    if (schema["anyOf"] is JsonArray branches)
    {
        // The polymorphic fallback branch (the plain base type) has no "type" discriminator property.
        for (var i = branches.Count - 1; i >= 0; i--)
        {
            if (branches[i]?["properties"]?["type"] is null)
            {
                branches.RemoveAt(i);
            }
        }

        schema.Remove("type");
    }

    schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
    schema["title"] = name;

    var path = Path.Combine(outputDir, $"{name}.json");
    File.WriteAllText(path, schema.ToJsonString(writeOptions) + "\n");
    Console.WriteLine(path);
}

return 0;
