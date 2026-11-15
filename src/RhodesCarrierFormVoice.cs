using System;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;

namespace EndfieldDoK.MapUnitLab
{
    // Read-only, native-event-time eligibility. DeploymentState admits only the
    // exact private carrier type; a revealed ally or a pooled view cannot opt in.
    internal static class RhodesCarrierFormVoice
    {
        internal const string Prefix="rhodes.carrier.form.";

        internal static bool ActionSupported(string action)
        {return action=="sel"||action=="atk"||action=="atm";}

        // SourceLevel is the committed hardware epoch. Research completion or
        // a changing target level alone never makes the new form claim readiness.
        internal static string Form(RhodesDeploymentSnapshot state)
        {
            if(state.Branch<0||state.Branch>2||state.SourceLevel<0||state.SourceLevel>2||
                state.TargetLevel<state.SourceLevel||state.TargetLevel>2||state.Kind<0||state.Kind>5)
                return null;
            if(state.Branch==0)
                return state.SourceLevel==0&&state.TargetLevel==0&&state.Kind==0&&
                    !state.DeckReady&&!state.ForeReady&&!state.MainReady&&!state.AftReady?"base":null;
            if(state.SourceLevel==0)
                return state.Kind==1&&state.TargetLevel>=1&&state.StartTick>=0&&
                    !state.DeckReady&&!state.ForeReady&&!state.MainReady&&!state.AftReady?"base":null;
            if(state.Kind==1||state.Kind==4)return null;
            if(state.Branch==1)
                return state.Kind<=3&&state.DeckReady?"aviation":null;
            // Kind5 only deploys the aft battery: the fore battery is usable.
            return (state.Kind==0||state.Kind==2||state.Kind==5)&&state.ForeReady?"gunnery":null;
        }

        internal static bool TryGet(VoiceOverEvent ev,bool direct,CommanderID local,out string form)
        {
            form=null;
            if(ev==null||direct||!ActionSupported(ev.Action)||
                !string.Equals(ev.PrimarySummary,"carrier",StringComparison.OrdinalIgnoreCase)||
                local.Equals(CommanderID.None)||!RhodesResearch.IsDeclaredOwner(local))return false;
            UnitView view;
            if(!UnitViewDiscovery.TryGet(ev.PositionalEntity,out view)||object.ReferenceEquals(view,null)||
                !view.EntityID.Equals(ev.PositionalEntity)||!view.CommanderID.Equals(local))return false;
            RhodesDeploymentSnapshot state;
            if(!RhodesDeploymentState.TryGet(ev.PositionalEntity,local,out state))return false;
            form=Form(state);return form!=null;
        }
    }
}
