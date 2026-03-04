using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;


namespace SolidworksAPI;

public class SWOdooBomExport
{
    public class BomNode
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public int Quantity { get; set; }
        public bool IsAssembly { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public List<BomNode> Children { get; set; } = new List<BomNode>();
    }

    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Connect to SOLIDWORKS
                var swApp = Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")) as SldWorks;
                if (swApp == null)
                {
                    Console.WriteLine("Unable to connect to SOLIDWORKS.");
                    return;
                }

                swApp.Visible = true;

                // Get active document
                var swModel = swApp.ActiveDoc as ModelDoc2;
                if (swModel == null)
                {
                    Console.WriteLine("No active document found.");
                    return;
                }
                AssemblyDoc swAssembly = (AssemblyDoc)swModel;
                swAssembly.ResolveAllLightWeightComponents(true);

                // Pass SW Assembly Use Componts in BuildBomTree
                object[] rootComp = (object[])swAssembly.GetComponents(false);
                Console.WriteLine($"ROOT LENGTH: {rootComp.Length}");

                // Build BOM recursively
                BomNode swBOMTree = BuildBomTree(rootComp[0] as Component2, 1);

                // Export to JSON
                string json = JsonSerializer.Serialize(swBOMTree, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Should be 15 Children for 1399
                string outputPath = $"C:/Users/tanner.troumbley/PycharmProjects/Solidworks API/{Path.GetFileNameWithoutExtension(swModel.GetTitle())}-tree.json";
                File.WriteAllText(outputPath, json);
                Console.WriteLine($"BOM exported to: {outputPath}");    
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        static BomNode BuildBomTree(Component2 comp, int quantity)
        // Change to Assembly and loop through components. Try to be recursive for assemblies.
        {
            if (comp == null || comp.IsSuppressed()) return null;
            string compPath = comp.GetPathName();

            if (string.IsNullOrEmpty(compPath)) return null;
            bool isAssembly = comp.GetModelDoc2() is AssemblyDoc;
 
            BomNode node = new BomNode
            {
                Name = Path.GetFileNameWithoutExtension(compPath),
                Quantity = quantity,
                IsAssembly = isAssembly,
            };

            ExtractCustomProperties(comp, node.Properties);

            if (isAssembly)
            {

                Dictionary<string, (Component2 comp, int qty)> childMap = new Dictionary<string, (Component2, int)>(StringComparer.OrdinalIgnoreCase);

                // object[] rootComp = (object[])swAssembly.GetComponents(false);
                object children = comp.GetChildren();
                Console.WriteLine($"CHildren: {children} for {Path.GetFileNameWithoutExtension(compPath)}");

                if (children != null)
                {
                    foreach (Component2 child in children as object[])
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
                        BomNode childNode = BuildBomTree(kvp.Value.comp, kvp.Value.qty);
                        if (childNode != null) node.Children.Add(childNode);
                    }
                }
            }
            return node;
        }

        static void ExtractCustomProperties(Component2 comp, Dictionary<string, string> props)
        {
            try
            {
                ModelDoc2 model = (ModelDoc2)comp.GetModelDoc2();
                if (model == null) return;
 
                CustomPropertyManager propMgr = model.Extension.CustomPropertyManager[""]; 
                string [] propNames = (string[]) propMgr.GetNames();
                foreach (string key in propNames)
                {
                    string valOut;
                    string resolvedVal;

                    propMgr.Get4(key, true, out valOut, out resolvedVal);

                    if (!string.IsNullOrEmpty(valOut)) 
                        props[key] = valOut;

                    else 
                        props[key] = resolvedVal;    
                }
            }
            catch
            {
                // Ignore property extraction errors
            }
        }
    }
}
