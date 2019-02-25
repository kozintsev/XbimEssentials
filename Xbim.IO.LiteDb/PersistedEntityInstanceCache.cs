using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LiteDB;
using Xbim.Common;
using Xbim.Common.Exceptions;

namespace Xbim.IO.LiteDb
{
    public class PersistedEntityInstanceCache : IDisposable
    {
        private string _databaseName;
        private readonly LiteDbModel _model;
        private LiteDatabase _instance;

        private readonly IEntityFactory _factory;

        protected ConcurrentDictionary<int, IPersistEntity> ModifiedEntities = new ConcurrentDictionary<int, IPersistEntity>();

        public PersistedEntityInstanceCache(LiteDbModel model, IEntityFactory factory)
        {
            _factory = factory;
            _instance = CreateInstance("XbimInstance");
            //_lockObject = new object();
            _model = model;
            //_entityTables = new EsentEntityCursor[MaxCachedEntityTables];
            //_geometryTables = new EsentCursor[MaxCachedGeometryTables];
        }

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

        public void Dispose()
        {
            _instance?.Dispose();
        }
    }
}
