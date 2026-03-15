using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MedievalArchitecture;

namespace MedievalArchitecture
{
    public class BEBehaviorGate
    {
        protected bool toggled;
        protected MeshData mesh;
        protected Cuboidf[] boxesClosed, boxesOpened;

        public int AttachedFace;
        public int RotDeg;

        public float RotRad => RotDeg * GameMath.DEG2RAD;

        //protected BlockBehaviorGate gateBh;

        public Cuboidf[] ColSelBoxes => toggled ? boxesOpened : boxesClosed;
        public bool Toggled => toggled;

    }
}
