namespace EndfieldDoK.MapUnitLab
{
    // Native reserve plus reserved points is conserved during allocation.
    public static class RhodesPowerBudget
    {
        // A completed expansion grants only the gap to its authorized total.
        public static int GrantRequired(int total,int completedReactorLevel)
        {
            if(completedReactorLevel<0||completedReactorLevel>5)return -1;
            int expected=RhodesPowerProfile.CapacityForResearchLevel(completedReactorLevel);
            bool known=false;
            for(int stage=0;stage<=completedReactorLevel;stage++)
                if(total==RhodesPowerProfile.CapacityForResearchLevel(stage))known=true;
            return known?expected-total:-1;
        }
    }
}
