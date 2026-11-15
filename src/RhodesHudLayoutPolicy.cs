using System;
using System.Collections.Generic;

namespace EndfieldDoK.MapUnitLab
{
    // Pure string policy, independent of Unity and native UI object lifetime.
    internal static class RhodesHudLayoutPolicy
    {
        internal static readonly string[] Contexts={"Default","Support","Armored","Ranged","Strike"};
        internal const string MenuPrefix="Ability_EF_Rhodes_Context_";
        internal static string Menu(string context){return MenuPrefix+context;}
        internal static int MenuHotkey(string context)
        {
            switch(context){case "Support":return 'e';case "Armored":return 'b';case "Ranged":return 't';case "Strike":return 'i';case "Default":return 27;default:throw new ArgumentException("Unknown context");}
        }
        internal static string ProductPage(string unit)
        {
            switch(unit)
            {
                case "RI_U01_HARVESTER":case "RI_U02_FIELD_SURVEY":case "RI_U03_ENGINEERING":return "Support";
                case "RI_U04_LIGHT_ESCORT":case "RI_U05_SUPPRESSION":return "Armored";
                case "RI_U06_RAILGUN":case "RI_U07_AIR_DEFENCE":case "RI_U09_ARTILLERY":return "Ranged";
                case "RI_U08_TILT_STRIKE":case "EF_Rhodes_Interceptor_MP":case "EF_Rhodes_HeavyBomber_MP":return "Strike";
                default:throw new ArgumentException("Unclassified Rhodes product: "+unit);
            }
        }
        internal static char ProductionKey(string unit)
        {
            switch(unit)
            {
                case "RI_U01_HARVESTER":case "RI_U04_LIGHT_ESCORT":case "RI_U06_RAILGUN":case "RI_U08_TILT_STRIKE":return 'j';
                case "RI_U02_FIELD_SURVEY":case "RI_U05_SUPPRESSION":case "RI_U07_AIR_DEFENCE":case "EF_Rhodes_Interceptor_MP":return 'k';
                case "RI_U03_ENGINEERING":case "RI_U09_ARTILLERY":case "EF_Rhodes_HeavyBomber_MP":return 'n';
                default:throw new ArgumentException("Unbound Rhodes product: "+unit);
            }
        }
        internal static string[] Page(string context,IDictionary<string,string> products)
        {
            if(Array.IndexOf(Contexts,context)<0)throw new ArgumentException("Unknown native HUD context: "+context);
            string[] result=new string[20];for(int i=0;i<result.Length;i++)result[i]=string.Empty;
            if(context=="Default")
            {
                result[10]=Menu("Support");result[11]=Menu("Armored");result[12]=Menu("Ranged");result[13]=Menu("Strike");
            }
            else
            {
                string[] units=context=="Support"?new string[]{"RI_U01_HARVESTER","RI_U02_FIELD_SURVEY","RI_U03_ENGINEERING"}:
                    context=="Armored"?new string[]{"RI_U04_LIGHT_ESCORT","RI_U05_SUPPRESSION"}:
                    context=="Ranged"?new string[]{"RI_U06_RAILGUN","RI_U07_AIR_DEFENCE","RI_U09_ARTILLERY"}:
                    new string[]{"RI_U08_TILT_STRIKE","EF_Rhodes_Interceptor_MP","EF_Rhodes_HeavyBomber_MP"};
                for(int i=0;i<units.Length;i++){string key;if(products.TryGetValue(units[i],out key))result[10+i]=key;}
                result[14]=Menu("Default");
            }
            return result;
        }
        internal static string[] AircraftTypes(string[] native,string fighter,string rotor,string append)
        {
            List<string> result=new List<string>();
            if(native!=null)foreach(string key in native)
                if(key!=fighter&&key!=rotor)result.Add(key);
            if(!string.IsNullOrEmpty(append))result.Add(append);
            return result.ToArray();
        }
        internal static bool Equal(string[] a,string[] b)
        {
            if(object.ReferenceEquals(a,b))return true;
            if(a==null||b==null||a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;
            return true;
        }
    }
}
