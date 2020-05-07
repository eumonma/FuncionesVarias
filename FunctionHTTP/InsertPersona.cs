using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;


namespace FunctionHTTP
{
    public static class InsertPersona
    {
        private const string TableName = "MyTable";

        
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
                PartitionKey = "TODO",
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
            return new OkObjectResult(todo);
        }
    }
}
