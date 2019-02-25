using System;
using Xbim.Common;

namespace Xbim.IO.LiteDb
{
    public class XbimReadTransaction : ITransaction
    {
        protected LiteDbModel Model;
        protected bool InTransaction;

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public string Name { get; set; }
        public void Commit()
        {
            throw new NotImplementedException();
        }

        public void RollBack()
        {
            throw new NotImplementedException();
        }

        public void DoReversibleAction(Action doAction, Action undoAction, IPersistEntity entity, ChangeType changeType, int property)
        {
            throw new NotImplementedException();
        }

        public event EntityChangedHandler EntityChanged;
        public event EntityChangingHandler EntityChanging;

        protected virtual void OnEntityChanged(IPersistEntity entity, ChangeType change, int property)
        {
            var handler = EntityChanged;
            handler?.Invoke(entity, change, property);
        }

        protected virtual void OnEntityChanging(IPersistEntity entity, ChangeType change, int property)
        {
            var handler = EntityChanging;
            handler?.Invoke(entity, change, property);
        }
    }
}
