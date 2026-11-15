namespace EndfieldDoK.MapUnitLab
{
    // Immutable cached UI data. No Unity references or live native lookups.
    public struct RhodesTiltrotorFlightStatus
    {
        public readonly string PhaseLabel;
        public readonly float PhaseAGL,GeometryBottomAGL,ViewAGL;
        public readonly bool HasVisualSample,Grounded,LandingRequested;
        internal RhodesTiltrotorFlightStatus(string label,float phase,float geometry,float view,bool sample,bool grounded,bool landing)
        {PhaseLabel=label;PhaseAGL=phase;GeometryBottomAGL=geometry;ViewAGL=view;HasVisualSample=sample;Grounded=grounded;LandingRequested=landing;}
    }
}
