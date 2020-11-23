using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PD2BundleDavServer
{
    public class GenericProgress
    {
        public string Operation;
        public int Total;
        public int Current;
        public bool Done;

        public GenericProgress()
        {
            Operation = "";
            Total = -1;
            Current = -1;
            Done = false;
        }

        public GenericProgress(string op, int curr, int total)
        {
            Operation = op;
            Total = total;
            Current = curr;
            Done = false;
        }

        public static GenericProgress Indefinite(string op, int current = -1) => new GenericProgress { Operation = op, Total = -1, Current = current };
    }
}
