namespace EndfieldDoK.MapUnitLab
{
    // Pure decision shared by the native bridge and isolated regression tests.
    public static class RhodesOperatorIdentityRules
    {
        public static int AllowedSourceCount(int candidates,bool livingIdentity,bool nativeReservationOrQueue)
        {return candidates<=0||livingIdentity||nativeReservationOrQueue?0:1;}
    }
}
