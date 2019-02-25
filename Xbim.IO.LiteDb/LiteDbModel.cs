using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Exceptions;
using Xbim.Common.Federation;
using Xbim.Common.Geometry;
using Xbim.Common.Metadata;
using Xbim.Common.Step21;

namespace Xbim.IO.LiteDb
{
    public class LiteDbModel : IModel, IFederatedModel, IDisposable
    {
        private IEntityFactory _factory;
        public IEntityFactory Factory => _factory;
        private bool _disposed;

        //private XbimInstanceCollection InstancesLocal { get; set; }

        protected PersistedEntityInstanceCache InstanceCache;
        internal PersistedEntityInstanceCache Cache
        {
            get { return InstanceCache; }
        }

        public LiteDbModel(IEntityFactory factory)
        {
            Init(factory);
        }

        protected void Init(IEntityFactory factory)
        {
            _factory = factory;
            InstanceCache = new PersistedEntityInstanceCache(this, factory);
           // InstancesLocal = new XbimInstanceCollection(this);
            var r = new Random();
            UserDefinedId = (short)r.Next(short.MaxValue); // initialise value at random to reduce chance of duplicates
            Metadata = ExpressMetaData.GetMetadata(factory.GetType().Module);
            ModelFactors = new XbimModelFactors(Math.PI / 180, 1e-3, 1e-5);
        }

        public void Dispose()
        {
            Dispose(true);
            // Take yourself off the Finalization queue 
            // to prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                try
                {
                    // If disposing equals true, dispose all managed 
                    // and unmanaged resources.
                    if (disposing)
                    {
                        //managed resources
                        Close();
                    }
                    //unmanaged, mostly esent related
                    //if (_geometryStore != null) _geometryStore.Dispose();
                    InstanceCache.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
            _disposed = true;
        }

        /// <summary>
        /// Closes the current model and releases all resources and instances
        /// </summary>
        public virtual void Close()
        {
            
        }

        public ILogger Logger { get; set; }
        public int UserDefinedId { get; set; }
        public object Tag { get; set; }
        public IGeometryStore GeometryStore { get; }
        public IStepFileHeader Header { get; }
        public bool IsTransactional { get; }
        public IList<XbimInstanceHandle> InstanceHandles { get; }
        public IEntityCollection Instances { get; }
        public bool Activate(IPersistEntity entity)
        {
            if (entity.Activated)
                return true;

            try
            {
                lock (entity)
                {
                    //check again in the lock
                    if (entity.Activated)
                        return true;

                    //activate and set the flag
                    InstanceCache.Activate(entity);
                    FlagSetter.SetActivationFlag(entity, true);
                    return true;
                }
            }
            catch (Exception e)
            {
                throw new XbimInitializationFailedException(string.Format("Failed to activate #{0}={1}", entity.EntityLabel, entity.ExpressType.ExpressNameUpper), e);
            }
        }

        public void Delete(IPersistEntity entity)
        {
            throw new NotImplementedException();
        }

        public ITransaction BeginTransaction(string name)
        {
            if (InverseCache != null)
                throw new XbimException("Transaction can't be open when cache is in operation.");
            
            try
            {
                //check if write permission upgrade is required               
                var txn = new XbimReadWriteTransaction(this, name);
                CurrentTransaction = txn;
                return txn;
            }
            catch (Exception e)
            {

                throw new XbimException("Failed to create ReadWrite transaction", e);
            }
        }

        /// <summary>
        /// Weak reference allows garbage collector to collect transaction once it goes out of the scope
        /// even if it is still referenced from model. This is important for the cases where the transaction
        /// is both not commited and not rolled back either.
        /// </summary>
        private WeakReference _transactionReference;

        public ITransaction CurrentTransaction
        {
            get
            {
                if (_transactionReference == null || !_transactionReference.IsAlive)
                    return null;
                return _transactionReference.Target as ITransaction;
            }
            internal set
            {
                if (value == null)
                {
                    _transactionReference = null;
                    return;
                }
                if (_transactionReference == null)
                    _transactionReference = new WeakReference(value);
                else
                    _transactionReference.Target = value;
            }
        }

        public ExpressMetaData Metadata { get; private set; }
        public IModelFactors ModelFactors { get; private set; }

        public T InsertCopy<T>(T toCopy, XbimInstanceHandleMap mappings, PropertyTranformDelegate propTransform, bool includeInverses,
            bool keepLabels) where T : IPersistEntity
        {
            throw new NotImplementedException();
        }

        public void ForEach<TSource>(IEnumerable<TSource> source, Action<TSource> body) where TSource : IPersistEntity
        {
            throw new NotImplementedException();
        }

        public event NewEntityHandler EntityNew;
        public event ModifiedEntityHandler EntityModified;
        public event DeletedEntityHandler EntityDeleted;
        public IInverseCache BeginInverseCaching()
        {
            throw new NotImplementedException();
        }

        public IInverseCache InverseCache { get; }
        public IEntityCache BeginEntityCaching()
        {
            throw new NotImplementedException();
        }

        public IEntityCache EntityCache { get; }
        public XbimSchemaVersion SchemaVersion { get; }
        public IModel ReferencingModel { get; }
        public IEnumerable<IReferencedModel> ReferencedModels { get; }
        public void AddModelReference(IReferencedModel model)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyEntityCollection FederatedInstances { get; }
        public IList<XbimInstanceHandle> FederatedInstanceHandles { get; }

        internal void HandleEntityChange(ChangeType changeType, IPersistEntity entity, int property)
        {
            switch (changeType)
            {
                case ChangeType.New:
                    EntityNew?.Invoke(entity);
                    break;
                case ChangeType.Deleted:
                    EntityDeleted?.Invoke(entity);
                    break;
                case ChangeType.Modified:
                    EntityModified?.Invoke(entity, property);
                    if (entity != null)
                        //Ass entity to 'Modified' collection. This is the single point of access where all changes go through
                        //so it is the best place to keep the track reliably.
                        Cache.AddModified(entity);

                    break;
                default:
                    throw new ArgumentOutOfRangeException("changeType", changeType, null);
            }
        }
    }
}
