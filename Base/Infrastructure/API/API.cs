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

        [GET("ListRoute", false)]
        private string[] ListRoute()
        {
            return APIService.Instance.ListRoute();
        }
    }
}
