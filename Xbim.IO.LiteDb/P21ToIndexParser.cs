using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xbim.IO.Parser;

namespace Xbim.IO.LiteDb
{
    public enum P21ParseAction
    {
        BeginList, //0
        EndList, //1
        BeginComplex, //2
        EndComplex, //3
        SetIntegerValue, //4
        SetHexValue, //5
        SetFloatValue, //6
        SetStringValue, //7
        SetEnumValue, //8
        SetBooleanValue, //9
        SetNonDefinedValue, //0x0A
        SetOverrideValue, //x0B
        BeginNestedType, //0x0C
        EndNestedType, //0x0D
        EndEntity, //0x0E
        NewEntity, //0x0F
        SetObjectValueUInt16,
        SetObjectValueInt32,
        SetObjectValueInt64
    }

    public class P21ToIndexParser : P21Parser, IDisposable
    {
        private readonly PersistedEntityInstanceCache _modelCache;
        const int TransactionBatchSize = 100;
        private int _entityCount = 0;
        private readonly int _codePageOverride = -1;
        private readonly long _streamSize = -1;

        Task _cacheProcessor;
        Task _storeProcessor;

        private BinaryWriter _binaryWriter;

        private BlockingCollection<Tuple<int, short, List<int>, byte[], bool>> _toStore;
        private BlockingCollection<Tuple<int, Type, byte[]>> _toProcess;

        internal P21ToIndexParser(Stream inputP21, long streamSize, PersistedEntityInstanceCache cache, int codePageOverride = -1)
            : base(inputP21)
        {

            _modelCache = cache;
            _entityCount = 0;
            _streamSize = streamSize;
            _codePageOverride = codePageOverride;
        }

        protected override void CharacterError()
        {
            throw new NotImplementedException();
        }

        protected override void BeginParse()
        {
            _binaryWriter = new BinaryWriter(new MemoryStream(0x7FFF));
            _toStore = new BlockingCollection<Tuple<int, short, List<int>, byte[], bool>>(512);
            if (_modelCache.IsCaching)
            {
                _toProcess = new BlockingCollection<Tuple<int, Type, byte[]>>();
                _cacheProcessor = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        // Consume the BlockingCollection 
                        while (!_toProcess.IsCompleted)
                        {
                            Tuple<int, Type, byte[]> h;
                            if (_toProcess.TryTake(out h))
                                _modelCache.GetOrCreateInstanceFromCache(h.Item1, h.Item2, h.Item3);
                        }
                    }
                    catch (InvalidOperationException)
                    {

                    }
                }
                    );

            }
            _storeProcessor = Task.Factory.StartNew(() =>
            {

                //using (var transaction = _table.BeginLazyTransaction())
                //{
                //    while (!_toStore.IsCompleted)
                //    {
                //        try
                //        {
                //            Tuple<int, short, List<int>, byte[], bool> h;
                //            if (_toStore.TryTake(out h))
                //            {
                //                _table.AddEntity(h.Item1, h.Item2, h.Item3, h.Item4, h.Item5, transaction);
                //                if (_toStore.IsCompleted)
                //                    _table.WriteHeader(Header);
                //                long remainder = _entityCount % TransactionBatchSize;
                //                if (remainder == TransactionBatchSize - 1)
                //                {
                //                    transaction.Commit();
                //                    transaction.Begin();
                //                }
                //            }
                //        }
                //        catch (SystemException)
                //        {

                //            // An InvalidOperationException means that Take() was called on a completed collection
                //            //OperationCanceledException can also be called

                //        }
                //    }
                //    transaction.Commit();
                //}
            }
            );
        }

        protected override void EndParse()
        {
            throw new NotImplementedException();
        }

        protected override void BeginHeader()
        {
            throw new NotImplementedException();
        }

        protected override void EndHeader()
        {
            throw new NotImplementedException();
        }

        protected override void BeginScope()
        {
            throw new NotImplementedException();
        }

        protected override void EndScope()
        {
            throw new NotImplementedException();
        }

        protected override void EndSec()
        {
            throw new NotImplementedException();
        }

        protected override void BeginList()
        {
            throw new NotImplementedException();
        }

        protected override void EndList()
        {
            throw new NotImplementedException();
        }

        protected override void BeginComplex()
        {
            throw new NotImplementedException();
        }

        protected override void EndComplex()
        {
            throw new NotImplementedException();
        }

        protected override void SetType(string entityTypeName)
        {
            throw new NotImplementedException();
        }

        protected override void NewEntity(string entityLabel)
        {
            throw new NotImplementedException();
        }

        protected override void EndEntity()
        {
            throw new NotImplementedException();
        }

        protected override void EndHeaderEntity()
        {
            throw new NotImplementedException();
        }

        protected override void SetIntegerValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override void SetHexValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override void SetFloatValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override void SetStringValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override void SetEnumValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override void SetBooleanValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override void SetNonDefinedValue()
        {
            throw new NotImplementedException();
        }

        protected override void SetOverrideValue()
        {
            throw new NotImplementedException();
        }

        protected override void SetObjectValue(string value)
        {
            throw new NotImplementedException();
        }

        protected override void EndNestedType(string value)
        {
            throw new NotImplementedException();
        }

        protected override void BeginNestedType(string value)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
