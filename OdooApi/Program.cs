
using OdooApi;

namespace SolidworksUpload
{
    class Program
    {
        private static readonly string LocalCachePath = "secrets_cache.json";
        // Key Vault URL (replace with your vault URI)
        private static readonly string KeyVaultUrl = "https://<your-key-vault-name>.vault.azure.net/";

    
        static async Task Main()
        {
            var SecureValues = new SecureCredentials(LocalCachePath, KeyVaultUrl);
            string url = await SecureValues.CheckValue("Odoo_URL");
            string dbName = await SecureValues.CheckValue("Odoo_DB");
            string username = await SecureValues.CheckValue("Odoo_Username");
            string password = await SecureValues.CheckValue("Odoo_Password");

            var client = new OdooApiClient(url, dbName, username, password);
            try
            {
                await client.AuthenticateAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Authentication Error. {ex}");
            }

            var partners = await client.SearchReadAsync("res.partner", [], ["name"], 5);
            Console.WriteLine(partners); 
            
            var products =  await client.SearchReadAsync("product.product", [], ["default_code"], 5);
            Console.WriteLine(products);
        }
    }
}