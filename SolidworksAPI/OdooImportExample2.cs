using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OdooBOMImporter2
{
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

            // Read the entire BOM JSON
            string bomJson = await File.ReadAllTextAsync("BOM.json");

            // Send to Odoo in one transaction
            var result = await client.CallOdooAsync("bom.importer", "import_bom_json", bomJson);

            Console.WriteLine("Odoo Response: " + JsonSerializer.Serialize(result));
        }
    }
}
