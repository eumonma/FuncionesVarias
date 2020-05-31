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
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Net;
using System.Net.Http.Headers;

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
        public static async Task<IActionResult> InsertaBlob(
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
                var fileData = await file.ReadAsByteArrayAsync();

                try
                {
                    var upload = await UploadFileToStorage(fileData, newFileName + ".");
                    uploadsurls.Add(upload);
                }
                catch (Exception ex)
                {
                    log.LogError(ex.Message);
                    return new BadRequestObjectResult("Error al subir: " + oldFileName);
                }
            }
            return uploadsurls != null
                ? (ActionResult)new OkObjectResult(uploadsurls)
                : new BadRequestObjectResult("Erro al subir el Blob");
        }


        private static async Task<string> UploadFileToStorage(byte[] fileStream, string fileName)
        {
            //StorageCredentials storageCredentials = new StorageCredentials("<AccountName>", "KeyValue");
            StorageCredentials storageCredentials = new StorageCredentials("almacenamiento1fotos", "dRKSB+/Hpdb1HtmTQJ25xmyxo3XSPV6Qd4t7JZIIf+lG8d1r7MQXIGd+ZdIP765cWPzmR6FqdU5NthnGHILJqA==");

            CloudStorageAccount storageAccount = new CloudStorageAccount(storageCredentials, true);

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference("fotos1equipo");

            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);

            // Upload the file
            await blockBlob.UploadFromByteArrayAsync(fileStream, 0, fileStream.Length);

            blockBlob.Properties.ContentType = "image/jpg";
            await blockBlob.SetPropertiesAsync();

            return blockBlob.Uri.ToString();
        }

        [FunctionName("DownloadImage")]
        public static async Task<HttpResponseMessage> DownloadImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "download/{fileName}")]HttpRequestMessage req,
            string fileName,
            ILogger log)
        {

            log.LogInformation("trigger function processed a request.");

            StorageCredentials storageCredentials = new StorageCredentials("almacenamiento1fotos", "dRKSB+/Hpdb1HtmTQJ25xmyxo3XSPV6Qd4t7JZIIf+lG8d1r7MQXIGd+ZdIP765cWPzmR6FqdU5NthnGHILJqA==");
            CloudStorageAccount storageAccount = new CloudStorageAccount(storageCredentials, true);
            CloudBlobContainer container = storageAccount.CreateCloudBlobClient().GetContainerReference("fotos1equipo");
            //var fileName = "Nombre de la imagen";
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);

            HttpResponseMessage message = new HttpResponseMessage(HttpStatusCode.OK);

            Stream blobStream = await blockBlob.OpenReadAsync();


            message.Content = new StreamContent(blobStream);
            message.Content.Headers.ContentLength = blockBlob.Properties.Length;
            message.StatusCode = HttpStatusCode.OK;
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(blockBlob.Properties.ContentType);
            message.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = $"CopyOf_{blockBlob.Name}",
                Size = blockBlob.Properties.Length
            };
            return message;

        }


        [FunctionName("DeleteImage")]
        public static async Task<bool> DeleteImage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete/{fileName}")]HttpRequestMessage req,
        string fileName,
        ILogger log)
        {

            log.LogInformation("trigger function processed a request.");

            StorageCredentials storageCredentials = new StorageCredentials("almacenamiento1fotos", "dRKSB+/Hpdb1HtmTQJ25xmyxo3XSPV6Qd4t7JZIIf+lG8d1r7MQXIGd+ZdIP765cWPzmR6FqdU5NthnGHILJqA==");
            CloudStorageAccount storageAccount = new CloudStorageAccount(storageCredentials, true);
            CloudBlobContainer container = storageAccount.CreateCloudBlobClient().GetContainerReference("fotos1equipo");
            //var fileName = "Nombre de la imagen";
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);

            var blobDeleted = await blockBlob.DeleteIfExistsAsync();

            return blobDeleted;

        }

    }

}
