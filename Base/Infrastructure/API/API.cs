using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Base.Core;
using Base.Services.APIService;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace API
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
    internal class V1 : WpfBehaviourSingleton<V1>
    {
        [POST("selectTabIndex", true,
            Summary = "Select a navigation tab by its index.",
            Description = "Navigates to the page/tab at the given zero-based position in the navigation list. " +
                "Body: { \"index\": integer }. Use ListTabs to discover the available tabs and their order.")]
        private void SelectTabIndex(int index)
        {
            Main.SelectTabIndex(index);
        }

        [POST("SelectTabByName", true,
            Summary = "Select a navigation tab by its display name.",
            Description = "Navigates to the page/tab whose display name matches. " +
                "Body: { \"name\": string } — must match a name returned by ListTabs. " +
                "Returns 200 when the tab is found and selected, or 404 when no tab with that name exists.")]
        private ApiResponse SelectTabByName(string name)
        {
            bool success = Main.SelectTabByName(name);

            return success ? new ApiResponse
            {
                Status = 200,
                Data = $"Tab '{name}' selected successfully."
            } : new ApiResponse
            {
                Status = 404,
                Data = $"Tab '{name}' not found."
            };
        }

        [GET("GetCurrentTab", true,
            Summary = "Get the currently selected tab.",
            Description = "Returns the display name of the page/tab that is currently selected. Takes no parameters.")]
        private ApiResponse GetCurrentTab()
        {
            var currentTab = Main.GetCurrentTab();
            return new ApiResponse
            {
                Status = 200,
                Data = currentTab
            };
        }

        [GET("ListTabs", true,
            Summary = "List all navigation tabs.",
            Description = "Returns the display names of every page/tab in the navigation, in order. Takes no " +
                "parameters. Use these names with SelectTabByName, or their positions with selectTabIndex.")]
        private ApiResponse ListTabs()
        {
            var tabNames = Main.ListTabs().ToArray();
            return new ApiResponse
            {
                Status = 200,
                Data = tabNames
            };
        }

        [GET("ListRoute", false,
            Summary = "List all API routes as readable strings.",
            Description = "Lists every registered API route as human-readable strings (verb, path, parameter " +
                "names and types, and the handler method). Takes no parameters. For machine-readable " +
                "descriptions and JSON Schemas of each route, use the schema endpoint instead.")]
        private string[] ListRoute()
        {
            return APIService.Instance.ListRoute();
        }

        [GET("schema", false,
            Summary = "Get a structured documentation manifest for every route.",
            Description = "Returns a structured documentation manifest for every registered route — verb, path, " +
                "description, summary, and a JSON Schema for the accepted inputs (and outputs where known). " +
                "Takes no parameters. Intended for MCP clients so tools carry real descriptions and schemas " +
                "instead of parsed strings.")]
        private object[] Schema()
        {
            return APIService.Instance.ListSchema();
        }
    }
}
