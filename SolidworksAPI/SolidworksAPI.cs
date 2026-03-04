using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using OdooApi;

namespace SolidworksAPI;

public class SWOdooBomImport
{
    public class BomNode
    {
        public required string Name { get; set; }
        public int Quantity { get; set; }
        public bool IsAssembly { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public List<BomNode> Components { get; set; } = new List<BomNode>();
    }

    class Program
    {
        private static readonly string LocalCachePath = "secrets_cache.json";
        // Key Vault URL (replace with your vault URI) Unable to get vault for myself so commenting it out. Update this and OdooApiclient for to use it.
        // private static readonly string KeyVaultUrl = "https://<your-key-vault-name>.vault.azure.net/";
        private static readonly string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private static readonly string logFilePath = Path.Combine(logDirectory, "SWOdooImport.log");
        private static readonly SemaphoreSlim logLock = new SemaphoreSlim(1, 1);


        static async Task Main(string[] args)
        {
            try
            {
                // Connect to SOLIDWORKS
                if (Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")) is not SldWorks swApp)
                {
                    Logger.Error("Unable to connect to SOLIDWORKS.");
                    await Logger.ShutdownAsync(); // Gracefully stop logger
                    return;
                }

                swApp.Visible = true;

                // Get active document
                if (swApp.ActiveDoc is not ModelDoc2 swModel)
                {
                    Logger.Error("No active document found in Solidworks.");
                    await Logger.ShutdownAsync(); // Gracefully stop logger
                    return;
                }

                BomNode swBOMTree;
                int docType = swModel.GetType();
                if (docType == (int)swDocumentTypes_e.swDocASSEMBLY)
                {

                    AssemblyDoc swAssembly = (AssemblyDoc)swModel;
                    // Need to resolve Lightweight to get all the data needed.
                    swAssembly.ResolveAllLightWeightComponents(true);

                    Component2 rootComp = swAssembly.GetEditTargetComponent();
                    
                    // Build BOM recursively
                    swBOMTree = BuildBomTree(rootComp, 1);
                }
                else
                {
                    swBOMTree = new BomNode
                    {
                        Name = Path.GetFileNameWithoutExtension(swModel.GetTitle()),
                        Quantity = 1,
                        IsAssembly = false,  
                    };
                    ExtractCustomProperties(swModel, swBOMTree.Properties);

                }

                // Export to JSON
                string json = JsonSerializer.Serialize(swBOMTree, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await PostOdoo(json);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }
   
        static async Task PostOdoo(string bomJson)
        {
            var SecureValues = new SecureCredentials(LocalCachePath);
            string url = await SecureValues.CheckValue("Odoo_URL");
            string dbName = await SecureValues.CheckValue("Odoo_DB");
            string username = await SecureValues.CheckValue("Odoo_Username");
            string password = await SecureValues.CheckValue("Odoo_Password");

            var client = new OdooApiClient(url, dbName, username, password);
            try
            {
                await client.AuthenticateAsync();
            }
            catch
            {
                // Logger in OdooApi gets the authenticaton error.
            }

            await client.ExecuteKwAsync("bom.importer", "import_bom_json", [bomJson]);
        }

        static BomNode? BuildBomTree(Component2 comp, int quantity)
        {
            if (comp == null || comp.IsSuppressed()) return null;
            if (comp.IsVirtual) return null;

            string compPath = comp.GetPathName();

            if (string.IsNullOrEmpty(compPath)) return null;
            bool isAssembly = comp.GetModelDoc2() is AssemblyDoc;
           

            BomNode node = new BomNode
            {
                Name = Path.GetFileNameWithoutExtension(compPath),
                Quantity = quantity,
                IsAssembly = isAssembly,
            };

            ModelDoc2 model = (ModelDoc2)comp.GetModelDoc2();
            if (model != null) ExtractCustomProperties(model, node.Properties);

            if (isAssembly)
            {
                Dictionary<string, (Component2 comp, int qty)> childMap = new Dictionary<string, (Component2, int)>(StringComparer.OrdinalIgnoreCase);

                object swchildren = comp.GetChildren();

                if (swchildren is object[] children)
                {
                    foreach (Component2 child in children.Cast<Component2>())
                    {
                        if (child == null || child.IsSuppressed()) continue;
                        string childPath = child.GetPathName();

                        if (string.IsNullOrEmpty(childPath)) continue;

                        if (!childMap.ContainsKey(childPath))
                            childMap[childPath] = (child, 0);

                        childMap[childPath] = (child, childMap[childPath].qty + 1);
                    }

                    foreach (var kvp in childMap)
                    {
                        BomNode? childNode = BuildBomTree(kvp.Value.comp, kvp.Value.qty);
                        if (childNode != null) node.Components.Add(childNode);
                    }
                }
            }
            return node;
        }

        static void ExtractCustomProperties(ModelDoc2 model, Dictionary<string, string> props)
        {
            try
            { 
                CustomPropertyManager propMgr = model.Extension.CustomPropertyManager[""]; 
                string [] propNames = (string[]) propMgr.GetNames();
                foreach (string key in propNames)
                {
                    propMgr.Get4(key, true, out string valOut, out string resolvedVal);

                    if (!string.IsNullOrEmpty(valOut)) 
                        props[key] = valOut;

                    else 
                        props[key] = resolvedVal;    
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }
    }
}
