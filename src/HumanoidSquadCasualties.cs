using System;

namespace EndfieldDoK.MapUnitLab
{
    // Read-only health -> presentation members. This does not own health,
    // create entities, alter damage, change commands or compact formation slots.
    public sealed class HumanoidSquadCasualties
    {
        readonly bool[] Alive;
        readonly int[] DeathOrder;
        int DeathCursor;
        int[] AliveIndices;
        public int TotalMembers { get; private set; }
        public int AliveCount { get; private set; }
        public int[] CurrentAliveIndices { get { return (int[])AliveIndices.Clone(); } }

        public HumanoidSquadCasualties(int members,uint seed)
        {
            if(members<1)throw new ArgumentOutOfRangeException("members");
            TotalMembers=members;AliveCount=members;
            Alive=new bool[members];DeathOrder=new int[members];AliveIndices=new int[members];
            for(int member=0;member<members;member++){Alive[member]=true;DeathOrder[member]=member;AliveIndices[member]=member;}
            uint state=seed==0?0xA511E9B3u:seed;
            for(int index=members-1;index>0;index--)
            {
                int other=(int)(Next(ref state)%(uint)(index+1));
                int swap=DeathOrder[index];DeathOrder[index]=DeathOrder[other];DeathOrder[other]=swap;
            }
        }

        // Invalid max-health snapshots are ignored, so a not-yet-initialized
        // native maximum cannot accidentally kill the displayed squad.
        // Healing never resurrects members; pause changes no presentation state.
        // Return true only if this snapshot caused additional member deaths.
        public bool ObserveHealth(int currentHP,int maximumHP,bool paused)
        {
            if(paused||maximumHP<=0||AliveCount==0)return false;
            int clamped=currentHP<0?0:currentHP>maximumHP?maximumHP:currentHP;
            int desired=(int)(((long)clamped*TotalMembers+maximumHP-1L)/maximumHP);
            if(desired>=AliveCount)return false;
            while(AliveCount>desired)
            {
                int member=DeathOrder[DeathCursor++];
                Alive[member]=false;AliveCount--;
            }
            AliveIndices=new int[AliveCount];int at=0;
            for(int member=0;member<TotalMembers;member++)if(Alive[member])AliveIndices[at++]=member;
            return true;
        }

        public bool IsAlive(int member)
        {return member>=0&&member<Alive.Length&&Alive[member];}

        // Stateless mapping: repeated/native-preview sequence lookups do not
        // consume an extra turn. A changed roster is used immediately, so no
        // lookup can return a dead member. IDs remain their original model slots.
        // Callers must handle -1 (no living shooter / invalid sequence).
        public int MemberForNativeRound(long sequence)
        {
            if(sequence<=0||AliveCount==0)return -1;
            return AliveIndices[(int)((sequence-1L)%AliveCount)];
        }

        static uint Next(ref uint state)
        {uint value=state;value^=value<<13;value^=value>>17;value^=value<<5;state=value;return value;}
    }
}
