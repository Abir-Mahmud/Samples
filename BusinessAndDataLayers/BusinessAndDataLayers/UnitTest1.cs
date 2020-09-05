using BusinessLayerLib;
using DomainLib;
using EntityFrameworkCoreGetSQL;
using EntityGraphQL.Schema;
using ExpressionFromGraphQLLib;
using LiteDB;
using LiteDBLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RepoDb;
using RepoDbLayer;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace BusinessAndDataLayers
{
    public class DemoController
    {
        private readonly WhereAsync _whereAsync;

        public DemoController(WhereAsync whereAsync)
        {
            _whereAsync = whereAsync;
        }

        public async Task GetAsync()
        {
            var orderRecords = _whereAsync.GetAsync<OrderRecord>(o => o.Id == "123");
        }
    }

    [TestClass]
    public partial class UnitTest1
    {
        private const string LiteDbFileName = "MyData.db";
        #region Fields

        private Mock<WhereAsync> _mockGet;
        private Mock<SaveAsync> _mockSave;
        private Mock<DeleteAsync> _mockDelete;
        private BusinessLayer _businessLayer;
        private readonly Person _bob = new Person { Key = new Guid("087aca6b-61d4-4d94-8425-1bdfb34dab38"), Name = "Bob" };
        private readonly string _id = Guid.NewGuid().ToString().Replace("-", "*");
        private bool _customDeleting = false;
        private bool _customDeleted = false;
        private bool _customBefore = false;
        private bool _customAfter = false;
        private Expression _getOrderByIdPredicate;
        #endregion

        #region Tests

        [TestMethod]
        public async Task TestGetEntityFramework()
        {
            await CreateOrdersDb();

            using var ordersDbContext = new OrdersDbContext();
            var entityFrameworkDataLayer = new EntityFrameworkDataLayer(ordersDbContext);
            var asyncEnumerable = await entityFrameworkDataLayer.GetAsync((Expression<Func<OrderRecord, bool>>)_getOrderByIdPredicate);
            var returnValue = await asyncEnumerable.ToListAsync();
            Assert.AreEqual(1, returnValue.Count);
        }


        [TestMethod]
        public async Task TestGetEntityFrameworkViaGraphQL()
        {
            var schema = SchemaBuilder.FromObject<OrdersDbContext>();

            var expressionFromGraphQLProvider = new ExpressionFromGraphQLProvider(schema);

            var expression = expressionFromGraphQLProvider.GetExpression($@"orderRecord.where(id = ""{_id}"")");

            await CreateOrdersDb();

            using var ordersDbContext = new OrdersDbContext();
            var entityFrameworkDataLayer = new EntityFrameworkDataLayer(ordersDbContext);
            var asyncEnumerable = await entityFrameworkDataLayer.GetAsync((Expression<Func<OrderRecord, bool>>)expression);
            var returnValue = await asyncEnumerable.ToListAsync();
            Assert.AreEqual(1, returnValue.Count);
        }

        [TestMethod]
        public async Task TestGetDbLiteViaGraphQL()
        {
            using var db = SetupLiteDb();
            var schema = SchemaBuilder.FromObject<OrdersDbContext>();

            var expressionFromGraphQLProvider = new ExpressionFromGraphQLProvider(schema);

            var expression = expressionFromGraphQLProvider.GetExpression($@"orderRecord.where(id = ""{_id}"")");

            await CreateOrdersDb();

            var repoDbDataLayer = new LiteDbDataLayer(db);
            var asyncEnumerable = await repoDbDataLayer
                .GetAsync((Expression<Func<OrderRecord, bool>>)expression);

            var returnValue = await asyncEnumerable.ToListAsync();
            Assert.AreEqual(1, returnValue.Count);
        }

        //[TestMethod]
        //public async Task TestGetRepoDbViaGraphQL()
        //{
        //    await CreateOrdersDb();

        //    var schema = SchemaBuilder.FromObject<OrdersDbContext>();

        //    var expressionFromGraphQLProvider = new ExpressionFromGraphQLProvider(schema);

        //    var expression = expressionFromGraphQLProvider.GetExpression($@"orderRecord.where(id = ""{_id}"")");

        //    await CreateOrdersDb();

        //    using var connection = new SQLiteConnection(OrdersDbContext.ConnectionString);
        //    var repoDbDataLayer = new RepoDbDataLayer(connection);
        //    var asyncEnumerable = await repoDbDataLayer
        //        .WhereAsync((Expression<Func<OrderRecord, bool>>)expression);

        //    var returnValue = await asyncEnumerable.ToListAsync();
        //    Assert.AreEqual(1, returnValue.Count);
        //}

        [TestMethod]
        public async Task TestGetRepoDb()
        {
            await CreateOrdersDb();

            SqLiteBootstrap.Initialize();

            using var connection = new SQLiteConnection(OrdersDbContext.ConnectionString);
            var repoDbDataLayer = new RepoDbDataLayer(connection);
            var asyncEnumerable = await repoDbDataLayer.GetAsync((Expression<Func<OrderRecord, bool>>)_getOrderByIdPredicate);


            var returnValue = await asyncEnumerable.ToListAsync();
            Assert.AreEqual(1, returnValue.Count);
        }

        [TestMethod]
        public async Task TestGetLiteDb()
        {
            using var db = SetupLiteDb();
            var repoDbDataLayer = new LiteDbDataLayer(db);
            var asyncEnumerable = await repoDbDataLayer
                .GetAsync((Expression<Func<OrderRecord, bool>>)_getOrderByIdPredicate);

            var returnValue = await asyncEnumerable.ToListAsync();
            Assert.AreEqual(1, returnValue.Count);
        }

        [TestMethod]
        public async Task TestGetLiteDbWithBusinessLayer()
        {
            using var db = SetupLiteDb();
            var liteDbDataLayer = new LiteDbDataLayer(db);

            var businessLayer = new BusinessLayer(whereAsync: liteDbDataLayer.GetAsync);

            var getAsync = (WhereAsync)businessLayer.WhereAsync;

            var asyncEnumerable = getAsync.GetAsync((Expression<Func<OrderRecord, bool>>)_getOrderByIdPredicate);

            var returnValue = (await asyncEnumerable).ToListAsync().Result;
            Assert.AreEqual(1, returnValue.Count);
        }

        [TestMethod]
        public async Task TestGetWithBusinessLayer()
        {
            await CreateOrdersDb();

            SqLiteBootstrap.Initialize();

            using var connection = new SQLiteConnection(OrdersDbContext.ConnectionString);
            var repoDbDataLayer = new RepoDbDataLayer(connection);

            var businessLayer = new BusinessLayer(
                whereAsync: repoDbDataLayer.GetAsync,
                beforeGet: async (t, e) => { _customBefore = true; },
                afterGet: async (t, result) => { _customAfter = true; });

            WhereAsync whereAsync = businessLayer.WhereAsync;

            var asyncEnumerable = await whereAsync
                .GetAsync<OrderRecord>(o => o.Id == _id);


            var returnValue = await asyncEnumerable.ToListAsync();
            Assert.AreEqual(1, returnValue.Count);
            Assert.IsTrue(_customBefore && _customAfter);
        }


        [TestMethod]
        public async Task TestUpdating()
        {
            //Arrange

            //Return 1 person
            _mockGet.Setup(r => r(It.IsAny<Expression<Func<Person, bool>>>())).Returns(Task.FromResult<IAsyncEnumerable<object>>(new DummyPersonAsObjectAsyncEnumerable(true)));

            //Act
            var savedPerson = (Person)await _businessLayer.SaveAsync(_bob, true);

            //Assert

            //Verify custom business logic
            Assert.AreEqual("BobUpdatingUpdated", savedPerson.Name);

            //Verify update was called
            _mockSave.Verify(d => d(It.IsAny<Person>(), true), Times.Once);
        }

        [TestMethod]
        public async Task TestInserted()
        {
            //Act
            var savedPerson = (Person)await _businessLayer.SaveAsync(_bob, false);

            //Assert

            //Verify custom business logic
            Assert.AreEqual("BobInsertingInserted", savedPerson.Name);

            //Verify insert was called
            _mockSave.Verify(d => d(It.IsAny<Person>(), false), Times.Once);
        }

        [TestMethod]
        public async Task TestDeleted()
        {
            //Act
            await _businessLayer.DeleteAsync(typeof(Person), _bob.Key);

            //Verify insert was called
            _mockDelete.Verify(d => d(typeof(Person), _bob.Key), Times.Once);

            Assert.IsTrue(_customDeleted && _customDeleting);
        }

        [TestMethod]
        public async Task TestGet()
        {
            //Act
            WhereAsync whereAsync = _businessLayer.WhereAsync;
            var people = await whereAsync.GetAsync<Person>((p) => true);

            //Verify get was called
            _mockGet.Verify(d => d(It.IsAny<Expression<Func<Person, bool>>>()), Times.Once);

            Assert.IsTrue(_customBefore && _customAfter);

            //This returns an empty list by default
            Assert.AreEqual(0, (await people.ToListAsync()).Count);
        }


        #endregion
        #region Arrange
        [TestInitialize]
        public async Task TestInitialize()
        {
            _mockGet = new Mock<WhereAsync>();
            _mockSave = new Mock<SaveAsync>();
            _mockDelete = new Mock<DeleteAsync>();

            _mockSave.Setup(r => r(It.IsAny<object>(), true)).Returns(Task.FromResult<object>(_bob));
            _mockSave.Setup(r => r(It.IsAny<object>(), false)).Returns(Task.FromResult<object>(_bob));
            _mockGet.Setup(r => r(It.IsAny<Expression<Func<Person, bool>>>())).Returns(Task.FromResult<IAsyncEnumerable<object>>(new DummyPersonAsObjectAsyncEnumerable(false)));

            _getOrderByIdPredicate = _mockGet.Object.CreateQueryExpression<OrderRecord>(o => o.Id == _id);
            _businessLayer = GetBusinessLayer().businessLayer;
        }

        private async Task CreateOrdersDb()
        {
            await using var ordersDbContext = new OrdersDbContext();
            await ordersDbContext.OrderRecord.AddAsync(new OrderRecord { Id = _id });
            await ordersDbContext.SaveChangesAsync();
        }

        private LiteDatabase SetupLiteDb()
        {
            if (File.Exists(LiteDbFileName)) File.Delete(LiteDbFileName);
            var db = new LiteDatabase(LiteDbFileName);

            // Get a collection (or create, if doesn't exist)
            var orders = db.GetCollection<OrderRecord>("OrderRecords");

            // Create your new customer instance
            var order = new OrderRecord
            {
                Id = _id,
                Name = "John Doe"
            };

            // Insert new customer document (Id will be auto-incremented)
            orders.Insert(order);

            // Update a document inside a collection
            order.Name = "Jane Doe";

            orders.Update(order);

            // Index document using document Name property
            orders.EnsureIndex(x => x.Name);

            // Use LINQ to query documents (filter, sort, transform)
            var results = orders.Query()
                .Where(x => x.Name.StartsWith("J"))
                .OrderBy(x => x.Name)
                .Select(x => new { x.Name, NameUpper = x.Name.ToUpper() })
                .Limit(10)
                .ToList();

            // Let's create an index in phone numbers (using expression). It's a multikey index
            orders.EnsureIndex(x => x.Name);

            // and now we can query phones
            var r = orders.FindOne(x => x.Name.Contains("Jane"));

            //TODO: This shouldn't be necessary. We already inserted an order with the same id...
            orders = db.GetCollection<OrderRecord>();
            orders.Insert(new OrderRecord { Id = _id, Name = "123" });

            return db;
        }

        private (BusinessLayer businessLayer, ServiceProvider serviceProvider) GetBusinessLayer()
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddSingleton<Saving<Person>>(async (p, u) =>
            {
                if (u)
                {
                    p.Name += "Updating";
                }
                else
                {
                    p.Name += "Inserting";
                }
            })
            .AddSingleton<Saved<Person>>(async (p, u) =>
            {
                if (u)
                {
                    p.Name += "Updated";
                }
                else
                {
                    p.Name += "Inserted";
                }
            })
            .AddSingleton<Deleting<Person>>(async (key) =>
            {
                _customDeleting = key == _bob.Key;
            })
            .AddSingleton<Deleted<Person>>(async (key) =>
            {
                _customDeleted = key == _bob.Key;
            })
            .AddSingleton<BeforeGet<Person>>(async (query) =>
            {
                _customBefore = true;
            })
            .AddSingleton<AfterGet<Person>>(async (people) =>
            {
                _customAfter = true;
            });

            var serviceProvider = serviceCollection.BuildServiceProvider();

            var businessLayer = new BusinessLayer(
                _mockSave.Object,
                _mockGet.Object,
                _mockDelete.Object,
                async (type, key) =>
                {
                    var delegateType = typeof(Deleting<>).MakeGenericType(new[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new[] { key });
                },
                async (type, key, count) =>
                {
                    var delegateType = typeof(Deleted<>).MakeGenericType(new[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new[] { key });
                }, async (entity, isUpdate) =>
                {
                    var delegateType = typeof(Saving<>).MakeGenericType(new[] { entity.GetType() });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new[] { entity, isUpdate });
                },
                async (entity, isUpdate) =>
                {
                    var delegateType = typeof(Saved<>).MakeGenericType(new[] { entity.GetType() });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new[] { entity, isUpdate });
                },
                async (type, query) =>
                {
                    var delegateType = typeof(BeforeGet<>).MakeGenericType(new[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate.DynamicInvoke(new object[] { query });
                },
                async (type, items) =>
                {
                    var delegateType = typeof(AfterGet<>).MakeGenericType(new[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new[] { items });
                }
                );

            return (businessLayer, serviceProvider);
        }
        #endregion
    }
}
