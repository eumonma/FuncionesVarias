using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;


namespace FunctionHTTP
{
    public static class InsertPersona
    {
        private const string TableName = "MyTable";
        private const string KeyParticion = "PERSONA";


        public class Todo
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("n");
            public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
            public string Nombre { get; set; }
            public bool IsCompleted { get; set; }
        }

        public static TodoTableEntity ToTableEntity(this Todo todo)
        {
            return new TodoTableEntity()
            {
                PartitionKey = KeyParticion,
                RowKey = todo.Id,
                CreatedTime = todo.CreatedTime,
                IsCompleted = todo.IsCompleted,
                NombreEmpleado = todo.Nombre
            };
        }

        public class TodoTableEntity : TableEntity
        {
            public DateTime CreatedTime { get; set; }
            public string NombreEmpleado { get; set; }
            public bool IsCompleted { get; set; }
        }


        public static Todo ToTodo(this TodoTableEntity todo)
        {
            return new Todo()
            {
                Id = todo.RowKey,
                CreatedTime = todo.CreatedTime,
                IsCompleted = todo.IsCompleted,
                Nombre = todo.NombreEmpleado
            };
        }

        public class TodoCreateModel
        {
            public string Name { get; set; }
        }

        [FunctionName("InsertPersona")]
        //        [return: Table("MyTable")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [Table(TableName, Connection = "AzureWebJobsStorage")] IAsyncCollector<TodoTableEntity> todoTable,
             ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");


            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<TodoCreateModel>(requestBody);

            var todo = new Todo() { Nombre = data.Name };
            await todoTable.AddAsync(todo.ToTableEntity());
            //            return new OkObjectResult(todo);
            return new OkObjectResult("OK");
        }

        [FunctionName("RecuperaTabla")]
        public static async Task<IActionResult> GetTodos(
//            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Route)]HttpRequest req,
            [HttpTrigger(AuthorizationLevel.Anonymous, "get")]HttpRequest req,
            // unfortunately IQueryable<TodoTableEntity> binding not supported in functions v2
            [Table(TableName, Connection = "AzureWebJobsStorage")] CloudTable todoTable,
            ILogger log)
        {
            log.LogInformation("Getting todo list items");
            var query = new TableQuery<TodoTableEntity>();
            var segment = await todoTable.ExecuteQuerySegmentedAsync(query, null);
//            return new OkObjectResult(segment.Select(Mappings.ToTodo));
            return new OkObjectResult(segment.Select(ToTodo));
//            return new OkObjectResult(segment.ToTodo());
        }


        [FunctionName("RecuperaTablaByPartition")]
        public static async Task<IActionResult> GetByPartition(
//            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "/{id}")]HttpRequest req,
            [HttpTrigger(AuthorizationLevel.Anonymous, "get")]HttpRequest req,
            [Table(TableName, Connection = "AzureWebJobsStorage")] CloudTable todoTable,
            ILogger log)
        {


            var particion = req.Query["partition"];
            log.LogInformation("Getting por particion: " + particion);

            // Mezclar condiciones
            var query2 = new TableQuery<TodoTableEntity>().Where(
                TableQuery.CombineFilters(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "TODO"),
                    TableOperators.And,
                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.GreaterThan, "t")
                    )
                );

            // Una única condición
            var query = new TableQuery<TodoTableEntity>().Where(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, particion)
                );

            var segment = await todoTable.ExecuteQuerySegmentedAsync(query, null);
            return new OkObjectResult(segment.Select(ToTodo));
        }
    }
}
