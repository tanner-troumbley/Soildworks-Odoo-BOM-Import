using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OdooBOMImporter
{
    // -------------------------
    // Data Models (match BOM.json)
    // -------------------------
    public class CustomProperty
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string RawValue { get; set; }
        public string Scope { get; set; }
    }

    public class PartEntry
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public List<CustomProperty> Properties { get; set; } = new();
    }

    public class AssemblyBOM
    {
        public string AssemblyName { get; set; }
        public string AssemblyPath { get; set; }
        public List<PartEntry> Parts { get; set; } = new();
        public List<AssemblyBOM> SubAssemblies { get; set; } = new();
    }

    // -------------------------
    // Odoo JSON-RPC Client
    // -------------------------
    public class OdooClient
    {
        private readonly HttpClient _http;
        private readonly string _url;
        private readonly string _db;
        private readonly string _username;
        private readonly string _password;
        private int _uid;

        public OdooClient(string url, string db, string username, string password)
        {
            _http = new HttpClient();
            _url = url.TrimEnd('/');
            _db = db;
            _username = username;
            _password = password;
        }

        public async Task<bool> AuthenticateAsync()
        {
            var result = await JsonRpcAsync<object>("common", "login", _db, _username, _password);
            if (result is JsonElement el && el.ValueKind == JsonValueKind.Number)
            {
                _uid = el.GetInt32();
                return _uid > 0;
            }
            return false;
        }

        public async Task<object> CallOdooAsync(string model, string method, params object[] args)
        {
            return await JsonRpcAsync<object>("object", "execute_kw", _db, _uid, _password, model, method, args);
        }

        private async Task<T> JsonRpcAsync<T>(string service, string method, params object[] args)
        {
            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service,
                    method,
                    args
                },
                id = 1
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_url}/jsonrpc", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("error", out var error))
                throw new Exception(error.ToString());

            return doc.RootElement.GetProperty("result").Deserialize<T>();
        }
    }

    class Program
    {
        // Mapping from SOLIDWORKS property name → Odoo field name
        static readonly Dictionary<string, string> PROPERTY_FIELD_MAP = new()
        {
            { "Description", "description" },
            { "Material", "x_material" },
            { "Part Number", "default_code" },
            { "Weight", "x_weight" }
        };

        static async Task Main()
        {
            string odooUrl = "http://localhost:8069";
            string db = "odoo17db";
            string username = "admin";
            string password = "admin";

            var client = new OdooClient(odooUrl, db, username, password);

            if (!await client.AuthenticateAsync())
            {
                Console.WriteLine("[FATAL] Failed to authenticate with Odoo.");
                return;
            }

            // Validate fields exist
            var existingFields = await ValidateOdooFieldsAsync(client);

            // Load BOM JSON
            var bomJson = await File.ReadAllTextAsync("BOM.json");
            var rootBOM = JsonSerializer.Deserialize<AssemblyBOM>(bomJson);

            // Collect all BOMs in memory
            var bomsToCreate = new List<(int productId, List<Dictionary<string, object>> bomLines)>();
            await CollectBOMsAsync(client, rootBOM, existingFields, bomsToCreate);

            // Create all BOMs
            foreach (var (productId, bomLines) in bomsToCreate)
            {
                var tmplId = await GetProductTemplateIdAsync(client, productId);
                var bomId = await client.CallOdooAsync("mrp.bom", "create", new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "product_tmpl_id", tmplId },
                        { "product_qty", 1.0 },
                        { "type", "normal" },
                        { "bom_line_ids", bomLines.ConvertAll(line => new object[] { 0, 0, line }) }
                    }
                });
                Console.WriteLine($"[OK] Created BOM {bomId} for product ID {productId}");
            }
        }

        static async Task<HashSet<string>> ValidateOdooFieldsAsync(OdooClient client)
        {
            var fields = await client.CallOdooAsync("ir.model.fields", "search_read",
                new object[] { new object[] { new object[] { "model", "=", "product.product" } }, new string[] { "name" } });

            var existing = new HashSet<string>();
            if (fields is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in arr.EnumerateArray())
                {
                    if (field.TryGetProperty("name", out var name))
                        existing.Add(name.GetString());
                }
            }

            foreach (var kv in PROPERTY_FIELD_MAP)
            {
                if (!existing.Contains(kv.Value))
                    Console.WriteLine($"[ERROR] Odoo field '{kv.Value}' for SW property '{kv.Key}' does not exist.");
            }

            return existing;
        }

        static Dictionary<string, object> MapPropertiesToOdoo(List<CustomProperty> properties, HashSet<string> existingFields)
        {
            var data = new Dictionary<string, object>();
            foreach (var prop in properties)
            {
                if (PROPERTY_FIELD_MAP.TryGetValue(prop.Name, out var odooField))
                {
                    if (existingFields.Contains(odooField))
                        data[odooField] = prop.Value;
                    else
                        Console.WriteLine($"[ERROR] Cannot map '{prop.Name}' → '{odooField}' (field missing in Odoo).");
                }
            }
            return data;
        }

               static async Task<int> GetOrCreateProductAsync(OdooClient client, string name, List<CustomProperty> properties, HashSet<string> existingFields)
        {
            // Search for product by name
            var searchResult = await client.CallOdooAsync("product.product", "search",
                new object[] { new object[] { new object[] { "name", "=", name } } });

            int productId = 0;
            if (searchResult is JsonElement arr && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                // Product exists → update it
                productId = arr[0].GetInt32();
                var updateData = MapPropertiesToOdoo(properties, existingFields);
                if (updateData.Count > 0)
                {
                    await client.CallOdooAsync("product.product", "write", new object[] { new int[] { productId }, updateData });
                }
            }
            else
            {
                // Product does not exist → create it
                var createData = MapPropertiesToOdoo(properties, existingFields);
                createData["name"] = name;
                createData["type"] = "product";
                var createResult = await client.CallOdooAsync("product.product", "create", new object[] { createData });

                if (createResult is JsonElement idEl && idEl.ValueKind == JsonValueKind.Number)
                    productId = idEl.GetInt32();
            }

            return productId;
        }

        static async Task<int> GetProductTemplateIdAsync(OdooClient client, int productId)
        {
            var readResult = await client.CallOdooAsync("product.product", "read",
                new object[] { new int[] { productId }, new string[] { "product_tmpl_id" } });

            if (readResult is JsonElement arr && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                var tmplArr = arr[0].GetProperty("product_tmpl_id");
                if (tmplArr.ValueKind == JsonValueKind.Array && tmplArr.GetArrayLength() > 0)
                    return tmplArr[0].GetInt32();
            }
            throw new Exception($"Unable to get product_tmpl_id for product ID {productId}");
        }

        static async Task CollectBOMsAsync(OdooClient client, AssemblyBOM assembly, HashSet<string> existingFields,
            List<(int productId, List<Dictionary<string, object>> bomLines)> bomsToCreate)
        {
            // Create or update assembly product
            int assemblyProductId = await GetOrCreateProductAsync(client, assembly.AssemblyName, new List<CustomProperty>(), existingFields);

            var bomLines = new List<Dictionary<string, object>>();

            // Process parts
            foreach (var part in assembly.Parts)
            {
                int partProductId = await GetOrCreateProductAsync(client, part.FileName, part.Properties, existingFields);
                bomLines.Add(new Dictionary<string, object>
                {
                    { "product_id", partProductId },
                    { "product_qty", 1.0 }
                });
            }

            // Add BOM for this assembly if it has parts
            if (bomLines.Count > 0)
            {
                bomsToCreate.Add((assemblyProductId, bomLines));
            }

            // Recursively process sub-assemblies
            foreach (var sub in assembly.SubAssemblies)
            {
                await CollectBOMsAsync(client, sub, existingFields, bomsToCreate);
            }
        }
    }
}
