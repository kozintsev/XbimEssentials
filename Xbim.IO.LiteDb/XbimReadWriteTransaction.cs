using Xbim.Common;

namespace Xbim.IO.LiteDb
{
    public class XbimReadWriteTransaction : XbimReadTransaction, ITransaction
    {
        //private int _pulseCount;

        internal XbimReadWriteTransaction(LiteDbModel model, string name = null)
        {
            Name = name;
            Model = model;
            InTransaction = true;
            //_pulseCount = 0;
        }
    }
}
