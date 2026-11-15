using System;
using BBI.Core.ComponentModel;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Per-instance presentation fault boundary. No simulation flags, queue or
    // native entity state is changed. A new pooled entity identity may retry;
    // the same failing entity cannot flood the log on every rendering frame.
    public abstract class IsolatedUnitPresentation:MonoBehaviour
    {
        bool PresentationFaulted;Entity FaultOwner;
        public bool PresentationFailed{get{return PresentationFaulted;}}
        public void QuarantineBinding(UnitView view,string service,Exception error)
        {DisablePresentation(view,service,error);}
        public void PrepareNativeSpawn(UnitView view)
        {
            if(!PresentationFaulted||view==null||FaultOwner.Equals(view.EntityID))return;
            PresentationFaulted=false;enabled=true;
        }
        protected void DisablePresentation(UnitView owner,string service,Exception error)
        {
            if(PresentationFaulted)return;PresentationFaulted=true;FaultOwner=Entity.None;
            try{if(owner!=null)FaultOwner=owner.EntityID;}catch{}
            Runtime.Log("UNIT_PRESENTATION_DISABLED service="+service+"; entity="+FaultOwner+"; authorityContinues=True; retry=new_entity; error="+error);
            try{enabled=false;}
            catch(Exception cleanup){Runtime.Log("UNIT_PRESENTATION_DISABLE_CLEANUP_FAILED service="+service+"; error="+cleanup);}
        }
    }
}
