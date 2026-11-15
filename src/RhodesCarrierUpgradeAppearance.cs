namespace EndfieldDoK.MapUnitLab
{
    // Remap the existing authored ornaments without altering the frozen model.
    internal static class RhodesCarrierUpgradeAppearance
    {
        internal static string ResearchOwner(string slot,string authoredResearch)
        {
            if(slot=="REPAIR_ADDON"&&authoredResearch==RhodesResearch.Repair2)return RhodesResearch.EngineeringRepair;
            if(slot=="CIWS_ADDON"&&authoredResearch==RhodesResearch.CIWS2)return RhodesResearch.ReactorCapacity3;
            if(slot=="DECK_ADDON"&&authoredResearch==RhodesResearch.Aviation2)return RhodesResearch.InterceptorService;
            if(slot=="DECK_ADDON"&&authoredResearch==RhodesResearch.Gunnery2)return RhodesResearch.Gunnery3;
            return authoredResearch;
        }

        internal static bool Eligible(string branch,int aviationLevel,int gunneryLevel,bool completed)
        {
            if(!completed)return false;
            if(branch=="aviation")return aviationLevel>0;
            // Charge may complete before the separate aft hardware research.
            if(branch=="gunnery")return gunneryLevel>=2;
            return branch=="common";
        }

        internal static int ObserveCompletion(bool initialized,bool previouslyShown,bool shown,int priorStart,int completedTick)
        {
            if(!shown||!initialized)return -1;
            return previouslyShown?priorStart:completedTick;
        }
    }
}
