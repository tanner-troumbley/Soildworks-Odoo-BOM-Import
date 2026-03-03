// Only works with Solidworks Installed.
using System.Runtime.InteropServices;
// using SolidWorks.Interop.sldworks;
// using SolidWorks.Interop.swconst;

namespace SolidWorksApiExample
{
    class Program
    {
        static void Main(string[] args)
        {
            SldWorks swApp = null;
            ModelDoc2 swModel = null;

            try
            {
                // Try to connect to a running SOLIDWORKS instance
                swApp = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
                Console.WriteLine("Connected to running SOLIDWORKS instance.");
            }
            catch (COMException)
            {
                // If not running, start a new instance
                try
                {
                    swApp = (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
                    swApp.Visible = true;
                    Console.WriteLine("Started new SOLIDWORKS instance.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error starting SOLIDWORKS: " + ex.Message);
                    return;
                }
            }

            try
            {
                // Example: Open a part file (update path to your file)
                string filePath = @"C:\Path\To\Your\Part.SLDPRT";
                int errors = 0, warnings = 0;

                swModel = swApp.OpenDoc6(
                    filePath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "", ref errors, ref warnings
                );

                if (swModel != null)
                {
                    Console.WriteLine("Opened file: " + swModel.GetTitle());
                    Console.WriteLine("Document type: " + swModel.GetType());
                    Console.WriteLine("Configuration count: " + swModel.GetConfigurationCount());
                }
                else
                {
                    Console.WriteLine("Failed to open file. Error code: " + errors);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error interacting with SOLIDWORKS: " + ex.Message);
            }
            finally
            {
                // Optional: Release COM objects
                if (swModel != null) Marshal.ReleaseComObject(swModel);
                if (swApp != null) Marshal.ReleaseComObject(swApp);
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}

