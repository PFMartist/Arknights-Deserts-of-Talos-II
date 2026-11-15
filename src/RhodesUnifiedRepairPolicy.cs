namespace EndfieldDoK.MapUnitLab
{
    // Pure decisions over native query results. No simulation or Unity state.
    public static class RhodesUnifiedRepairPolicy
    {
        public static bool IsVisibleRepair(string abilityName)
        {return abilityName=="Ability_EF_Rhodes_Repair";}
        public static bool ChooseStarboard(bool portInView,bool starboardInView,bool starboardIsNearer)
        {
            if(portInView!=starboardInView)return starboardInView;
            return starboardIsNearer;
        }
    }
}
