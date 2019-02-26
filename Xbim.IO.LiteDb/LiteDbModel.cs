using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private bool _deleteOnClose;

        private int _codePageOverrideForStepFiles = -1;

        private readonly ReferencedModelCollection _referencedModels = new ReferencedModelCollection();

        //private XbimInstanceCollection InstancesLocal { get; set; }

        /// <summary>
        /// Some applications do not comply with the standard and used the Windows code page for text. This property gives the possibility to override the character encoding when reading ifc.
        /// default value = -1 - by standard http://www.buildingsmart-tech.org/implementation/get-started/string-encoding/string-encoding-decoding-summary
        /// </summary>
        /// <example>
        /// model.CodePageOverride = Encoding.Default.WindowsCodePage;
        /// </example>
        public int CodePageOverride
        {
            get { return _codePageOverrideForStepFiles; }
            set { _codePageOverrideForStepFiles = value; }
        }

        protected PersistedEntityInstanceCache InstanceCache;
        internal PersistedEntityInstanceCache Cache
        {
            get { return InstanceCache; }
        }

        /// <summary>
        /// Only inherited models can call parameter-less constructor and it is their responsibility to 
        /// call Init() as the very first thing.
        /// </summary>
        internal LiteDbModel()
        {
            Logger = XbimLogging.CreateLogger<LiteDbModel>();
        }

        internal void InitialiseHeader(IStepFileHeader header)
        {
            _header = header;
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

        public string DatabaseName => InstanceCache.DatabaseName;

        /// <summary>
        /// Closes the current model and releases all resources and instances
        /// </summary>
        public virtual void Close()
        {
            var dbName = DatabaseName;
            ModelFactors = new XbimModelFactors(Math.PI / 180, 1e-3, 1e-5);
            Header = null;

            //if (_editTransactionEntityCursor != null)
            //    EndTransaction();
            //if (_geometryStore != null)
            //{
            //    _geometryStore.Dispose();
            //    _geometryStore = null;
            //}
            InstanceCache.Close();

            //dispose any referenced models
            foreach (var r in _referencedModels)
            {
                var model = r.Model;
                IDisposable refModel = model;
                refModel?.Dispose();
            }

            _referencedModels.Clear();

            try //try and tidy up if required
            {
                if (_deleteOnClose && File.Exists(dbName))
                {
                    File.Delete(dbName);
                    // Since Windows 10 Anniverary Edition JET FlushMap files are created for each XBIM
                    // https://docs.microsoft.com/en-us/windows/desktop/extensiblestorageengine/gg294069(v%3Dexchg.10)#flush-map-files
                    var flushMapFile = Path.ChangeExtension(dbName, ".jfm");
                    if (File.Exists(flushMapFile))
                    {
                        File.Delete(flushMapFile);
                    }

                }
            }
            catch (Exception)
            {
                // ignored
            }
            _deleteOnClose = false;
        }

        public virtual bool CreateFrom(Stream inputStream, long streamSize, StorageType streamType, string xbimDbName, 
            ReportProgressDelegate progDelegate = null, bool keepOpen = false, bool cacheEntities = false, bool deleteOnClose = false)
        {
            Close();

            if (streamType.HasFlag(StorageType.Ifc) ||
                streamType.HasFlag(StorageType.Stp))
            {
                Cache.ImportStep(xbimDbName, inputStream, streamSize, progDelegate, keepOpen, cacheEntities, _codePageOverrideForStepFiles);
            }

            _deleteOnClose = deleteOnClose;
            return true;
        }

        private IStepFileHeader _header;
        public IStepFileHeader Header
        {
            get { return _header; }
            set
            {
                _header = value;
                if (value == null) return;

                if (CurrentTransaction != null)
                {
                    //var cursor = GetTransactingCursor();
                    //cursor.WriteHeader(_header);
                }
                else
                {
                    //using (var txn = BeginTransaction("New header"))
                    //{
                    //    var cursor = GetTransactingCursor();
                    //    cursor.WriteHeader(_header);
                    //    txn.Commit();
                    //}
                }
                _header.PropertyChanged += (sender, args) =>
                {
                    //if (CurrentTransaction != null)
                    //{
                    //    var cursor = GetTransactingCursor();
                    //    cursor.WriteHeader(_header);
                    //}
                    //else
                    //{
                    //    using (var txn = BeginTransaction("Header changed"))
                    //    {
                    //        var cursor = GetTransactingCursor();
                    //        cursor.WriteHeader(_header);
                    //        txn.Commit();
                    //    }
                    //}
                };
            }
        }

        public ILogger Logger { get; set; }
        public int UserDefinedId { get; set; }
        public object Tag { get; set; }
        public IGeometryStore GeometryStore { get; }
        public bool IsTransactional { get; set; }
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
