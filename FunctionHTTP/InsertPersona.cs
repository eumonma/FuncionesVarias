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
using Microsoft.WindowsAzure.Storage;
using System.Net.Http;
using System.Collections.Generic;

namespace FunctionHTTP
{
    public static class InsertPersona
    {
        private const string TableName = "MyTable";
        private const string KeyParticion = "PERSONA";
        private const string routeDelete = "delete";
        private const string routeUpdate = "update";


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

        public class TodoUpdateModel
        {
            public string Nombre { get; set; }
            public bool IsCompleted { get; set; }
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
            [HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequest req,
            [Table(TableName, Connection = "AzureWebJobsStorage")] CloudTable todoTable,
            ILogger log)
        {
            log.LogInformation("Getting todo list items");
            var query = new TableQuery<TodoTableEntity>();
            var segment = await todoTable.ExecuteQuerySegmentedAsync(query, null);
//            return new OkObjectResult(segment.Select(Mappings.ToTodo));
            return new OkObjectResult(segment.Select(ToTodo));
        }


        [FunctionName("RecuperaTablaByPartition")]
        public static async Task<IActionResult> GetByPartition(
//            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "/{id}")]HttpRequest req,
            [HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequest req,
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



        [FunctionName("Table_DeleteTodo")]
        public static async Task<IActionResult> DeleteTodo(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = routeDelete + "/{id}")]HttpRequest req,
            [Table(TableName, Connection = "AzureWebJobsStorage")] CloudTable todoTable,
            ILogger log, string id)
        {

            log.LogInformation("Delete por particion: " + id);

            var particion = req.Query["partition"];
            log.LogInformation("Delete por particion: " + particion);
            var deleteEntity = new TableEntity { PartitionKey = particion, RowKey = id, ETag = "*" };
            var deleteOperation = TableOperation.Delete(deleteEntity);
            try
            {
                await todoTable.ExecuteAsync(deleteOperation);
            }
            catch (StorageException e) when (e.RequestInformation.HttpStatusCode == 404)
            {
                
                return new NotFoundResult();
            }
            return new OkResult();
        }


        [FunctionName("Table_UpdateTodo")]
        public static async Task<IActionResult> UpdateTodo(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = routeUpdate + "/{id}")]HttpRequest req,
            [Table(TableName, Connection = "AzureWebJobsStorage")] CloudTable todoTable,
            ILogger log, string id)
        {

            var particion = req.Query["partition"];
            log.LogInformation("Delete por particion: " + particion);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var updated = JsonConvert.DeserializeObject<TodoUpdateModel>(requestBody);

            var findOperation = TableOperation.Retrieve<TodoTableEntity>(particion, id);
            var findResult = await todoTable.ExecuteAsync(findOperation);
            if (findResult.Result == null)
            {
                return new NotFoundResult();
            }
            var existingRow = (TodoTableEntity)findResult.Result;

            existingRow.IsCompleted = updated.IsCompleted;
            if (!string.IsNullOrEmpty(updated.Nombre))
            {
                existingRow.NombreEmpleado = updated.Nombre;
            }

            var replaceOperation = TableOperation.Replace(existingRow);
            await todoTable.ExecuteAsync(replaceOperation);

            return new OkObjectResult(existingRow.ToTodo());
        }


        // Incorporar la capacidad de subir imágenes


        [FunctionName("UploadImage")]
        public static async Task Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload")]HttpRequestMessage req,
            ILogger log)
        {
            var provider = new MultipartMemoryStreamProvider();
         
            await req.Content.ReadAsMultipartAsync(provider);

            var files = provider.Contents;
            List<string> uploadsurls = new List<string>();
            foreach (var file in files)
            {
                var fileInfo = file.Headers.ContentDisposition;
                Guid guid = Guid.NewGuid();
                string oldFileName = fileInfo.FileName;
                string newFileName = guid.ToString();
                var fileExtension = oldFileName.Split('.').Last().Replace("\"", "").Trim();
                
            }




            //var file = provider.Contents.First();
            //var fileInfo = file.Headers.ContentDisposition;
            var fileData = await file.ReadAsByteArrayAsync();

            var newImage = new Image()
            {
                FileName = fileInfo.FileName,
                Size = fileData.LongLength,
                Status = ImageStatus.Processing
            };

            var imageName = await DataHelper.CreateImageRecord(newImage);
            if (!(await StorageHelper.SaveToBlobStorage(imageName, fileData)))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(imageName)
            };
        };

        public static async Task SaveToBlobStorage(string blobName, byte[] data)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnectionString);
            CloudBlobClient client = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("images");

            var blob = container.GetBlockBlobReference(blobName);
            await blob.UploadFromByteArrayAsync(data, 0, data.Length);

            return true;
        }

    }
}
