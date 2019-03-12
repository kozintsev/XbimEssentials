using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LiteDB;
using Xbim.Common;
using Xbim.Common.Exceptions;
using Xbim.Common.Metadata;

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
        protected ConcurrentDictionary<int, IPersistEntity> CreatedNew = new ConcurrentDictionary<int, IPersistEntity>();
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

        public bool Contains(IPersistEntity instance)
        {
            return Contains(instance.EntityLabel);
        }

        public bool Contains(int entityLabel)
        {
            if (_caching && _read.ContainsKey(entityLabel)) //check if it is cached
                return true;
            else //look in the database
            {
                //var entityTable = GetEntityTable();
                //try
                //{
                //    return entityTable.TrySeekEntityLabel(entityLabel);
                //}
                //finally
                //{
                //    FreeTable(entityTable);
                //}
                return false;
            }
        }

        /// <summary>
        /// returns the number of instances in the model
        /// </summary>
        /// <returns></returns>
        public long Count
        {
            get
            {
                //var entityTable = GetEntityTable();
                //try
                //{
                //    long dbCount = 1; //entityTable.RetrieveCount();
                //    if (_caching) dbCount += CreatedNew.Count;
                //    return dbCount;
                //}
                //finally
                //{
                //    FreeTable(entityTable);
                //}
                return 1;
            }
        }

        /// <summary>
        /// Returns an instance of the entity with the specified label,
        /// if the instance has already been loaded it is returned from the cache
        /// if it has not been loaded a blank instance is loaded, i.e. will not have been activated
        /// </summary>
        /// <param name="label"></param>
        /// <param name="loadProperties"></param>
        /// <param name="unCached"></param>
        /// <returns></returns>
        public IPersistEntity GetInstance(int label, bool loadProperties = false, bool unCached = false)
        {

            IPersistEntity entity;
            if (_caching && _read.TryGetValue(label, out entity))
                return entity;
            return GetInstanceFromStore(label, loadProperties, unCached);
        }

        /// <summary>
        /// Loads a blank instance from the database, do not call this before checking that the instance is in the instances cache
        /// If the entity has already been cached it will throw an exception
        /// This is not a undoable/reversable operation
        /// </summary>
        /// <param name="entityLabel">Must be a positive value of the label</param>
        /// <param name="loadProperties">if true the properties of the object are loaded  at the same time</param>
        /// <param name="unCached">if true the object is not cached, this is dangerous and can lead to object duplicates</param>
        /// <returns></returns>
        private IPersistEntity GetInstanceFromStore(int entityLabel, bool loadProperties = false, bool unCached = false)
        {
            //var entityTable = GetEntityTable();
            //try
            //{
            //    using (entityTable.BeginReadOnlyTransaction())
            //    {

            //        if (entityTable.TrySeekEntityLabel(entityLabel))
            //        {
            //            var currentIfcTypeId = entityTable.GetIfcType();
            //            if (currentIfcTypeId == 0) // this should never happen (there's a test for it, but old xbim files might be incorrectly identified)
            //                return null;
            //            IPersistEntity entity;
            //            if (loadProperties)
            //            {
            //                var properties = entityTable.GetProperties();
            //                entity = _factory.New(_model, currentIfcTypeId, entityLabel, true);
            //                if (entity == null)
            //                {
            //                    // this has been seen to happen when files attempt to instantiate abstract classes.
            //                    return null;
            //                }
            //                entity.ReadEntityProperties(this, new BinaryReader(new MemoryStream(properties)), unCached);
            //            }
            //            else
            //                entity = _factory.New(_model, currentIfcTypeId, entityLabel, false);
            //            if (_caching && !unCached)
            //                entity = _read.GetOrAdd(entityLabel, entity);
            //            return entity;
            //        }
            //    }
            //}
            //finally
            //{
            //    FreeTable(entityTable);
            //}
            return null;

        }

        internal XbimGeometryHandle GetGeometryHandle(int geometryLabel)
        {
            //var geometryTable = GetGeometryTable();
            //try
            //{
            //    return geometryTable.GetGeometryHandle(geometryLabel);
            //}
            //finally
            //{
            //    FreeTable(geometryTable);
            //}
            return new XbimGeometryHandle();
        }

        /// <summary>
        /// Creates a new instance
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        internal IPersistEntity CreateNew(Type t)
        {
            if (!_caching)
                throw new XbimException("XbimModel.BeginTransaction must be called before editing a model");
            //var cursor = _model.GetTransactingCursor();
            //var h = cursor.AddEntity(t);
            var entity = _factory.New(_model, t, 1, true) as IPersistEntity;
            //entity = _read.GetOrAdd(h.EntityLabel, entity);
            //ModifiedEntities.TryAdd(h.EntityLabel, entity);
            //CreatedNew.TryAdd(h.EntityLabel, entity);

            return entity;
        }

        /// <summary>
        /// Returns an enumeration of handles to all instances in the database and in the cache
        /// </summary>
        public IEnumerable<XbimInstanceHandle> InstanceHandles
        {
            get
            {
                //var entityTable = GetEntityTable();
                try
                {
                    //if (entityTable.TryMoveFirst()) // we have something
                    //{
                    //    do
                    //    {
                            yield return new XbimInstanceHandle(); //entityTable.GetInstanceHandle();
                    //    }
                    //    while (entityTable.TryMoveNext());
                    //}
                }
                finally
                {
                    //FreeTable(entityTable);
                }
            }
        }
        /// <summary>
        /// Returns an enumeration of handles to all instances in the database or the cache of specified type
        /// </summary>
        /// <returns></returns>
        public IEnumerable<XbimInstanceHandle> InstanceHandlesOfType<TIfcType>()
        {
            //var reqType = typeof(TIfcType);
            //var expressType = Model.Metadata.ExpressType(reqType);
            //var entityTable = GetEntityTable();
            //try
            //{
            //    foreach (var t in expressType.NonAbstractSubTypes)
            //    {
            //        XbimInstanceHandle ih;
            //        if (entityTable.TrySeekEntityType(t.TypeId, out ih))
            //        {
            //            yield return ih;
            //            while (entityTable.TryMoveNextEntityType(out ih))
            //            {
            //                yield return ih;
            //            }
            //        }
            //    }
            //}
            //finally
            //{
            //    FreeTable(entityTable);
            //}
            yield return new XbimInstanceHandle();
        }

        /// <summary>
        /// Enumerates of all instances of the specified type. The values are cached, if activate is true all the properties of the entity are loaded
        /// </summary>
        /// <typeparam name="TOType"></typeparam>
        /// <param name="activate">if true loads the properties of the entity</param>
        /// <param name="indexKey">if the entity has a key object, optimises to search for this handle</param>
        /// <param name="overrideType">if specified this parameter overrides the expressType used internally (but not TIfcType) for filtering purposes</param>
        /// <returns></returns>
        internal IEnumerable<TOType> OfType<TOType>(bool activate = false, int? indexKey = null, ExpressType overrideType = null) where TOType : IPersistEntity
        {
            //srl this needs to be removed, but preserves compatibility with old databases, the -1 should not be used in future
            int indexKeyAsInt;
            if (indexKey.HasValue) indexKeyAsInt = indexKey.Value; //this is lossy and needs to be fixed if we get large databases
            else indexKeyAsInt = -1;
            var eType = overrideType ?? Model.Metadata.ExpressType(typeof(TOType));

            // when searching for Interface types expressType is null
            //
            var typesToSearch = eType != null ?
                eType.NonAbstractSubTypes :
                Model.Metadata.TypesImplementing(typeof(TOType));

            var unindexedTypes = new HashSet<ExpressType>();

            //Set the IndexedClass Attribute of this class to ensure that seeking by index will work, this is a optimisation
            // Trying to look a class up by index that is not declared as indexable
            var entityLabels = new HashSet<int>();
            //var entityTable = GetEntityTable();
            try
            {
                //using (entityTable.BeginReadOnlyTransaction())
                //{
                //    foreach (var expressType in typesToSearch)
                //    {
                //        if (!expressType.IndexedClass) //if the class is indexed we can seek, otherwise go slow
                //        {
                //            unindexedTypes.Add(expressType);
                //            continue;
                //        }

                //        var typeId = expressType.TypeId;
                //        XbimInstanceHandle ih;
                //        if (entityTable.TrySeekEntityType(typeId, out ih, indexKeyAsInt) &&
                //            entityTable.TrySeekEntityLabel(ih.EntityLabel)) //we have the first instance
                //        {
                //            do
                //            {
                //                IPersistEntity entity;
                //                if (_caching && _read.TryGetValue(ih.EntityLabel, out entity))
                //                {
                //                    if (activate && !entity.Activated)
                //                    //activate if required and not already done
                //                    {
                //                        var properties = entityTable.GetProperties();
                //                        entity = _factory.New(_model, ih.EntityType, ih.EntityLabel, true);
                //                        entity.ReadEntityProperties(this,
                //                            new BinaryReader(new MemoryStream(properties)));
                //                    }
                //                    entityLabels.Add(entity.EntityLabel);
                //                    yield return (TOType)entity;
                //                }
                //                else
                //                {
                //                    if (activate)
                //                    {
                //                        var properties = entityTable.GetProperties();
                //                        entity = _factory.New(_model, ih.EntityType, ih.EntityLabel, true);
                //                        entity.ReadEntityProperties(this,
                //                            new BinaryReader(new MemoryStream(properties)));
                //                    }
                //                    else
                //                        // the attributes of this entity have not been loaded yet
                //                        entity = _factory.New(_model, ih.EntityType, ih.EntityLabel, false);

                //                    if (_caching) entity = _read.GetOrAdd(ih.EntityLabel, entity);
                //                    entityLabels.Add(entity.EntityLabel);
                //                    yield return (TOType)entity;
                //                }
                //            } while (entityTable.TryMoveNextEntityType(out ih) &&
                //                     entityTable.TrySeekEntityLabel(ih.EntityLabel));
                //        }
                //    }

                //}

                // we need to see if there are any objects in the cache that have not been written to the database yet.
                // 
                if (_caching) //look in the create new cache and find the new ones only
                {
                    foreach (var item in CreatedNew.Where(e => e.Value is TOType))
                    {
                        if (entityLabels.Add(item.Key))
                            yield return (TOType)item.Value;
                    }
                }
            }
            finally
            {
                //FreeTable(entityTable);
            }
            //we need to deal with types that are not indexed in the database in a single pass to save time
            // MC: Commented out this assertion because it just fires when inverse property is empty result.
            // Debug.Assert(indexKeyAsInt == -1, "Trying to look a class up by index key, but the class is not indexed");
            foreach (var item in InstancesOf<TOType>(unindexedTypes, activate, entityLabels))
                yield return item;


        }

        internal IEnumerable<IPersistEntity> OfType(string stringType, bool activate)
        {

            var ot = Model.Metadata.ExpressType(stringType.ToUpper());
            if (ot == null)
            {
                // it could be that we're searching for an interface
                //
                var implementingTypes = Model.Metadata.TypesImplementing(stringType);
                foreach (var implementingType in implementingTypes)
                {
                    foreach (var item in OfType<IPersistEntity>(activate: activate, overrideType: implementingType))
                        yield return item;
                }
            }
            else
            {
                foreach (var item in OfType<IPersistEntity>(activate: activate, overrideType: ot))
                    yield return item;

            }
        }

        private IEnumerable<TIfcType> InstancesOf<TIfcType>(IEnumerable<ExpressType> expressTypes, bool activate = false, HashSet<int> read = null) where TIfcType : IPersistEntity
        {
            var types = expressTypes as ExpressType[] ?? expressTypes.ToArray();
            if (types.Any())
            {
                var entityLabels = read ?? new HashSet<int>();
                //var entityTable = GetEntityTable();

                try
                {
                    //get all the type ids we are going to check for
                    var typeIds = new HashSet<short>();
                    foreach (var t in types)
                        typeIds.Add(t.TypeId);
                    //using (entityTable.BeginReadOnlyTransaction())
                    //{
                    //    entityTable.MoveBeforeFirst();
                    //    while (entityTable.TryMoveNext())
                    //    {
                    //        var ih = entityTable.GetInstanceHandle();
                    //        if (typeIds.Contains(ih.EntityTypeId))
                    //        {
                    //            IPersistEntity entity;
                    //            if (_caching && _read.TryGetValue(ih.EntityLabel, out entity))
                    //            {
                    //                if (activate && !entity.Activated)
                    //                //activate if required and not already done
                    //                {
                    //                    var properties = entityTable.GetProperties();
                    //                    entity.ReadEntityProperties(this,
                    //                        new BinaryReader(new MemoryStream(properties)));
                    //                    FlagSetter.SetActivationFlag(entity, true);
                    //                }
                    //                entityLabels.Add(entity.EntityLabel);
                    //                yield return (TIfcType)entity;
                    //            }
                    //            else
                    //            {
                    //                if (activate)
                    //                {
                    //                    var properties = entityTable.GetProperties();
                    //                    entity = _factory.New(_model, ih.EntityType, ih.EntityLabel, true);
                    //                    entity.ReadEntityProperties(this, new BinaryReader(new MemoryStream(properties)));
                    //                }
                    //                else
                    //                    //the attributes of this entity have not been loaded yet
                    //                    entity = _factory.New(_model, ih.EntityType, ih.EntityLabel, false);

                    //                if (_caching) entity = _read.GetOrAdd(ih.EntityLabel, entity);
                    //                entityLabels.Add(entity.EntityLabel);
                    //                yield return (TIfcType)entity;
                    //            }

                    //        }
                    //    }
                    //}
                    if (_caching) //look in the modified cache and find the new ones only
                    {
                        foreach (var item in CreatedNew.Where(e => e.Value is TIfcType))
                        //.ToList()) //force the iteration to avoid concurrency clashes
                        {
                            if (entityLabels.Add(item.Key))
                            {
                                yield return (TIfcType)item.Value;
                            }
                        }
                    }
                }
                finally
                {
                    //FreeTable(entityTable);
                }
            }
        }

        /// <summary>
        /// returns the number of instances of the specified type and its sub types
        /// </summary>
        /// <typeparam name="TIfcType"></typeparam>
        /// <returns></returns>
        public long CountOf<TIfcType>() where TIfcType : IPersistEntity
        {
            return CountOf(typeof(TIfcType));

        }
        /// <summary>
        /// returns the number of instances of the specified type and its sub types
        /// </summary>
        /// <param name="theType"></param>
        /// <returns></returns>
        private long CountOf(Type theType)
        {
            var entityLabels = new HashSet<int>();
            var expressType = Model.Metadata.ExpressType(theType);
            //var entityTable = GetEntityTable();
            var typeIds = new HashSet<short>();
            //get all the type ids we are going to check for
            foreach (var t in expressType.NonAbstractSubTypes)
                typeIds.Add(t.TypeId);
            //try
            //{

            //    XbimInstanceHandle ih;
            //    if (expressType.IndexedClass)
            //    {
            //        foreach (var typeId in typeIds)
            //        {
            //            if (entityTable.TrySeekEntityType(typeId, out ih))
            //            {
            //                do
            //                {
            //                    entityLabels.Add(ih.EntityLabel);
            //                } while (entityTable.TryMoveNextEntityType(out ih));
            //            }
            //        }
            //    }
            //    else
            //    {
            //        entityTable.MoveBeforeFirst();
            //        while (entityTable.TryMoveNext())
            //        {
            //            ih = entityTable.GetInstanceHandle();
            //            if (typeIds.Contains(ih.EntityTypeId))
            //                entityLabels.Add(ih.EntityLabel);
            //        }
            //    }
            //}
            //finally
            //{
            //    //FreeTable(entityTable);
            //}
            if (_caching) //look in the createdNew cache and find the new ones only
            {
                foreach (var entity in CreatedNew.Where(m => m.Value.GetType() == theType))
                    entityLabels.Add(entity.Key);

            }

            return 0; //entityLabels.Count;
        }

        public bool Any<TIfcType>() where TIfcType : IPersistEntity
        {
            var expressType = Model.Metadata.ExpressType(typeof(TIfcType));
            //var entityTable = GetEntityTable();
            try
            {
                foreach (var t in expressType.NonAbstractSubTypes)
                {
                    //XbimInstanceHandle ih;
                    //if (!entityTable.TrySeekEntityType(t.TypeId, out ih))
                        return true;
                }
            }
            finally
            {
                //FreeTable(entityTable);
            }
            return false;
        }

        public IEnumerable<T> Where<T>(Func<T, bool> condition) where T : IPersistEntity
        {
            return Where(condition, null, null);
        }

        public IEnumerable<T> Where<T>(Func<T, bool> condition, string inverseProperty, IPersistEntity inverseArgument) where T : IPersistEntity
        {
            var type = typeof(T);
            var et = Model.Metadata.ExpressType(type);
            List<ExpressType> expressTypes;
            if (et != null)
                expressTypes = new List<ExpressType> { et };
            else
            {
                //get specific interface implementations and make sure it doesn't overlap
                var implementations = Model.Metadata.ExpressTypesImplementing(type).Where(t => !t.Type.IsAbstract).ToList();
                expressTypes = implementations.Where(implementation => !implementations.Any(i => i != implementation && i.NonAbstractSubTypes.Contains(implementation))).ToList();
            }

            var canUseSecondaryIndex = inverseProperty != null && inverseArgument != null &&
                                       expressTypes.All(e => e.HasIndexedAttribute &&
                                                             e.IndexedProperties.Any(
                                                                 p => p.Name == inverseProperty));
            if (!canUseSecondaryIndex)
                return expressTypes.SelectMany(expressType => OfType<T>(true, null, expressType).Where(condition));

            //we can use a secondary index to look up
            var cache = _model._inverseCache;
            IEnumerable<T> result;
            if (cache != null && cache.TryGet(inverseProperty, inverseArgument, out result))
                return result;
            result = expressTypes.SelectMany(t => OfType<T>(true, inverseArgument.EntityLabel, t).Where(condition));
            var entities = result as IList<T> ?? result.ToList();
            if (cache != null)
                cache.Add(inverseProperty, inverseArgument, entities);
            return entities;
        }

        public void Dispose()
        {
            _instance?.Dispose();
        }
    }
}
