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
        [POST("selectTabIndex", true)]
        private void SelectTabIndex(int index)
        {
            Main.SelectTabIndex(index);
        }

        [POST("SelectTabByName", true)]
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

        [GET("GetCurrentTab", true)]
        private ApiResponse GetCurrentTab()
        {
            var currentTab = Main.GetCurrentTab();
            return new ApiResponse
            {
                Status = 200,
                Data = currentTab
            };
        }

        [GET("ListTabs", true)]
        private ApiResponse ListTabs()
        {
            var tabNames = Main.ListTabs().ToArray();
            return new ApiResponse
            {
                Status = 200,
                Data = tabNames
            };
        }

        [GET("ListRoute", false)]
        private string[] ListRoute()
        {
            return APIService.Instance.ListRoute();
        }

        /// <summary>
        /// Returns a structured documentation manifest for every registered route — verb, path,
        /// description and a JSON Schema for the accepted inputs (and outputs where known). Intended
        /// for MCP clients so tools carry real descriptions and schemas instead of parsed strings.
        /// </summary>
        [GET("schema", false)]
        private object[] Schema()
        {
            return APIService.Instance.ListSchema();
        }
    }
}
