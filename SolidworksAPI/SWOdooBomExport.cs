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
        public required string Name { get; set; }
        public int Quantity { get; set; }
        public bool IsAssembly { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public List<BomNode> Components { get; set; } = new List<BomNode>();
    }

    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Connect to SOLIDWORKS
                if (Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")) is not SldWorks swApp)
                {
                    Console.WriteLine("Unable to connect to SOLIDWORKS.");
                    return;
                }

                swApp.Visible = true;

                // Get active document
                if (swApp.ActiveDoc is not ModelDoc2 swModel)
                {
                    Console.WriteLine("No active document found.");
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
                string outputPath = $"C:/Users/tanner.troumbley/PycharmProjects/Solidworks API/{Path.GetFileNameWithoutExtension(swModel.GetTitle())}-tree.json";
                File.WriteAllText(outputPath, json);
                Console.WriteLine($"BOM exported to: {outputPath}");    
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
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
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
