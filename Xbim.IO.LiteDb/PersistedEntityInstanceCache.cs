using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using LiteDB;
using Xbim.Common;
using Xbim.Common.Exceptions;

namespace Xbim.IO.LiteDb
{
    public class PersistedEntityInstanceCache : IDisposable
    {
        private string _databaseName;
        private LiteDatabase _instance;
        private XbimDBAccess _accessMode;
        private bool _caching;
        private readonly LiteDbModel _model;

        private readonly IEntityFactory _factory;

        protected ConcurrentDictionary<int, IPersistEntity> ModifiedEntities = new ConcurrentDictionary<int, IPersistEntity>();
        private BlockingCollection<StepForwardReference> _forwardReferences = new BlockingCollection<StepForwardReference>();

        public PersistedEntityInstanceCache(LiteDbModel model, IEntityFactory factory)
        {
            _factory = factory;
            _instance = CreateInstance("XbimInstance");
            //_lockObject = new object();
            _model = model;
            //_entityTables = new EsentEntityCursor[MaxCachedEntityTables];
            //_geometryTables = new EsentCursor[MaxCachedGeometryTables];
        }

        public LiteDbModel Model => _model;

        private LiteDatabase CreateInstance(string instanceName, bool recovery = false, bool createTemporaryTables = false)
        {
            _databaseName = GetXbimTempDirectory();
            return new LiteDatabase(_databaseName);
        }


        internal static string GetXbimTempDirectory()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "Xbim." + Guid.NewGuid());
            if (!IsValidDirectory(ref tempDirectory))
            {
                tempDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Xbim." + Guid.NewGuid());
                if (!IsValidDirectory(ref tempDirectory))
                    throw new XbimException("Unable to initialise the Xbim database engine, no write access. Please set a location for the XbimTempDirectory in the config file");
            }
            return tempDirectory;
        }

        /// <summary>
        /// Checks the directory is writeable and modifies to be the full path
        /// </summary>
        /// <param name="tempDirectory"></param>
        /// <returns></returns>
        private static bool IsValidDirectory(ref string tempDirectory)
        {
            var tmpFileName = Guid.NewGuid().ToString();
            var fullTmpFileName = "";
            if (string.IsNullOrWhiteSpace(tempDirectory)) return false;
            tempDirectory = Path.GetFullPath(tempDirectory);
            var deleteDir = false;
            try
            {

                fullTmpFileName = Path.Combine(tempDirectory, tmpFileName);
                if (!Directory.Exists(tempDirectory))
                {
                    Directory.CreateDirectory(tempDirectory);
                    deleteDir = true;
                }
                using (File.Create(fullTmpFileName))
                { }
                return true;
            }
            catch (Exception)
            {
                tempDirectory = null;
            }
            finally
            {
                File.Delete(fullTmpFileName);
                if (deleteDir && tempDirectory != null) Directory.Delete(tempDirectory);
            }
            return false;
        }

        public void Activate(IPersistEntity entity)
        {
            //var bytes = GetEntityBinaryData(entity);
            //if (bytes != null)
            //    (entity as IInstantiableEntity).ReadEntityProperties(this, new BinaryReader(new MemoryStream(bytes)));
        }

        /// <summary>
        /// Adds an entity to the modified cache, if the entity is not already being edited
        /// Throws an exception if an attempt is made to edit a duplicate reference to the entity
        /// </summary>
        /// <param name="entity"></param>
        internal void AddModified(IPersistEntity entity)
        {
            //IPersistEntity editing;
            //if (modified.TryGetValue(entity.EntityLabel, out editing)) //it  already exists as edited
            //{
            //    if (!System.Object.ReferenceEquals(editing, entity)) //it is not the same object reference
            //        throw new XbimException("An attempt to edit a duplicate reference for #" + entity.EntityLabel + " error has occurred");
            //}
            //else
            ModifiedEntities.TryAdd(entity.EntityLabel, entity as IInstantiableEntity);
        }

        /// <summary>
        /// Imports the contents of the ifc file into the named database, the resulting database is closed after success, use LoadStep21 to access
        /// </summary>
        /// <param name="toImportIfcFilename"></param>
        /// <param name="progressHandler"></param>
        /// <param name="xbimDbName"></param>
        /// <param name="keepOpen"></param>
        /// <param name="cacheEntities"></param>
        /// <param name="codePageOverride"></param>
        /// <returns></returns>
        public void ImportStep(string xbimDbName, string toImportIfcFilename, ReportProgressDelegate progressHandler = null, bool keepOpen = false, bool cacheEntities = false, int codePageOverride = -1)
        {
            using (var reader = new FileStream(toImportIfcFilename, System.IO.FileMode.Open, FileAccess.Read))
            {
                ImportStep(xbimDbName, reader, reader.Length, progressHandler, keepOpen, cacheEntities, codePageOverride);
            }
        }

        internal bool IsCaching
        {
            get
            {
                return _caching;
            }
        }

        private readonly ConcurrentDictionary<int, IPersistEntity> _read = new ConcurrentDictionary<int, IPersistEntity>();

        /// <summary>
        /// Looks for this instance in the cache and returns it, if not found it creates a new instance and adds it to the cache
        /// </summary>
        /// <param name="label">Entity label to create</param>
        /// <param name="type">If not null creates an instance of this type, else creates an unknown Ifc Type</param>
        /// <param name="properties">if not null populates all properties of the instance</param>
        /// <returns></returns>
        public IPersistEntity GetOrCreateInstanceFromCache(int label, Type type, byte[] properties)
        {
            Debug.Assert(_caching); //must be caching to call this

            IPersistEntity entity;
            if (_read.TryGetValue(label, out entity)) return entity;

            if (type.IsAbstract)
            {
                //Model.Logger.LogError("Illegal Entity in the model #{0}, Type {1} is defined as Abstract and cannot be created", label, type.Name);
                return null;
            }

            return _read.GetOrAdd(label, l =>
            {
                var instance = _factory.New(_model, type, label, true);
                //instance.ReadEntityProperties(this, new BinaryReader(new MemoryStream(properties)), false, true);
                return instance;
            }); //might have been done by another
        }

        internal void ImportStep(string xbimDbName, Stream stream, long streamSize, ReportProgressDelegate progressHandler = null, bool keepOpen = false, bool cacheEntities = false, int codePageOverride = -1)
        {
            //CreateDatabase(xbimDbName);
            Open(xbimDbName, XbimDBAccess.Exclusive);
            //var table = GetEntityTable();
            if (cacheEntities) CacheStart();
            try
            {

                _forwardReferences = new BlockingCollection<StepForwardReference>();
                using (var part21Parser = new P21ToIndexParser(stream, streamSize, this, codePageOverride))
                {
                    if (progressHandler != null) part21Parser.ProgressStatus += progressHandler;
                    part21Parser.Parse();
                    _model.Header = part21Parser.Header;
                    if (progressHandler != null) part21Parser.ProgressStatus -= progressHandler;
                }

                //using (var transaction = table.BeginLazyTransaction())
                //{
                //    table.WriteHeader(_model.Header);
                //    transaction.Commit();
                //}
                //FreeTable(table);
                if (!keepOpen) Close();
            }
            catch (Exception)
            {
                //FreeTable(table);
                Close();
                File.Delete(xbimDbName);
                throw;
            }
        }

        /// <summary>
        /// Clears all contents from the cache and closes any connections
        /// </summary>
        public void Close()
        {
            // contributed by @Sense545
            //int refCount;
            //lock (OpenInstances)
            //{
            //    refCount = OpenInstances.Count(c => c.JetInstance == JetInstance);
            //}
            //var disposeTable = (refCount != 0); //only dispose if we have not terminated the instance
            //CleanTableArrays(disposeTable);
            //EndCaching();

            //if (_session == null)
            //    return;
            //Api.JetCloseDatabase(_session, _databaseId, CloseDatabaseGrbit.None);
            //lock (OpenInstances)
            //{
            //    OpenInstances.Remove(this);
            //    refCount = OpenInstances.Count(c => string.Compare(c.DatabaseName, DatabaseName, StringComparison.OrdinalIgnoreCase) == 0);
            //    if (refCount == 0) //only detach if we have no more references
            //        Api.JetDetachDatabase(_session, _databaseName);
            //}
            _databaseName = null;
            //_session.Dispose();
            //_session = null;
        }

        /// <summary>
        /// Starts a read cache
        /// </summary>
        internal void CacheStart()
        {
            _caching = true;
        }

        internal string DatabaseName
        {
            get => _databaseName;
            set => _databaseName = value;
        }

        internal void Open(string filename, XbimDBAccess accessMode = XbimDBAccess.Read)
        {
            _databaseName = Path.GetFullPath(filename); //success store the name of the DB file
            _accessMode = accessMode;
            _caching = false;
            //var entTable = GetEntityTable();
            try
            {
                //using (entTable.BeginReadOnlyTransaction())
                //{
                //    _model.InitialiseHeader(entTable.ReadHeader());
                //}
            }
            catch (Exception e)
            {
                Close();
                throw new XbimException("Failed to open " + filename, e);
            }
            finally
            {
                //FreeTable(entTable);
            }
        }

        public void Dispose()
        {
            _instance?.Dispose();
        }
    }
}
