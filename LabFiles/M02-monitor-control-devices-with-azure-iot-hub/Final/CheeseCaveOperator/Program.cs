// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Azure.EventHubs;
using Microsoft.Azure.Devices;
using Newtonsoft.Json;

namespace CheeseCaveOperator
{
    class Program
    {
        // Global variables.
        // The Event Hub-compatible endpoint.
        private readonly static string eventHubsCompatibleEndpoint = "<your event hub endpoint>";

        // The Event Hub-compatible name.
        private readonly static string eventHubsCompatiblePath = "<your event hub path>";
        private readonly static string iotHubSasKey = "<your event hub Sas key>";
        private readonly static string iotHubSasKeyName = "service";
        private static EventHubClient eventHubClient;
        // INSERT service client variable below here
        private static ServiceClient serviceClient;

        // INSERT registry manager variable below here
        private static RegistryManager registryManager;

        // Connection string for your IoT Hub.
        private readonly static string serviceConnectionString = "<your service connection string>";

        private readonly static string deviceId = "sensor-th-0055";

        // INSERT Main method below here
        public static void Main(string[] args)
        {
            ConsoleHelper.WriteColorMessage("Cheese Cave Operator\n", ConsoleColor.Yellow);

            // Create an EventHubClient instance to connect to the IoT Hub Event Hubs-compatible endpoint.
            var connectionString = new EventHubsConnectionStringBuilder(new Uri(eventHubsCompatibleEndpoint), eventHubsCompatiblePath, iotHubSasKeyName, iotHubSasKey);
            eventHubClient = EventHubClient.CreateFromConnectionString(connectionString.ToString());

            // Create a PartitionReceiver for each partition on the hub.
            var runtimeInfo = eventHubClient.GetRuntimeInformationAsync().GetAwaiter().GetResult();
            var d2cPartitions = runtimeInfo.PartitionIds;

            // INSERT register desired property changed handler code below here
            // A registry manager is used to access the digital twins.
            registryManager = RegistryManager.CreateFromConnectionString(serviceConnectionString);
            SetTwinProperties().Wait();

            // INSERT create service client instance below here
            // Create a ServiceClient to communicate with service-facing endpoint on your hub.
            serviceClient = ServiceClient.CreateFromConnectionString(serviceConnectionString);
            InvokeMethod().GetAwaiter().GetResult();

            // Create receivers to listen for messages.
            var tasks = new List<Task>();
            foreach (string partition in d2cPartitions)
            {
                tasks.Add(ReceiveMessagesFromDeviceAsync(partition));
            }

            // Wait for all the PartitionReceivers to finish.
            Task.WaitAll(tasks.ToArray());
        }

        // INSERT ReceiveMessagesFromDeviceAsync method below here
        // Asynchronously create a PartitionReceiver for a partition and then start reading any messages sent from the simulated client.
        private static async Task ReceiveMessagesFromDeviceAsync(string partition)
        {
            // Create the receiver using the default consumer group.
            var eventHubReceiver = eventHubClient.CreateReceiver("$Default", partition, EventPosition.FromEnqueuedTime(DateTime.Now));
            Console.WriteLine("Created receiver on partition: " + partition);

            while (true)
            {
                // Check for EventData - this methods times out if there is nothing to retrieve.
                var events = await eventHubReceiver.ReceiveAsync(100);

                // If there is data in the batch, process it.
                if (events == null) continue;

                foreach (EventData eventData in events)
                {
                    string data = Encoding.UTF8.GetString(eventData.Body.Array);

                    ConsoleHelper.WriteGreenMessage("Telemetry received: " + data);

                    foreach (var prop in eventData.Properties)
                    {
                        if (prop.Value.ToString() == "true")
                        {
                            ConsoleHelper.WriteRedMessage(prop.Key);
                        }
                    }
                    Console.WriteLine();
                }
            }
        }

        // INSERT InvokeMethod method below here
        // Handle invoking a direct method.
        private static async Task InvokeMethod()
        {
            try
            {
                var methodInvocation = new CloudToDeviceMethod("SetFanState") { ResponseTimeout = TimeSpan.FromSeconds(30) };
                string payload = JsonConvert.SerializeObject("On");

                methodInvocation.SetPayloadJson(payload);

                // Invoke the direct method asynchronously and get the response from the simulated device.
                var response = await serviceClient.InvokeDeviceMethodAsync(deviceId, methodInvocation);

                if (response.Status == 200)
                {
                    ConsoleHelper.WriteGreenMessage("Direct method invoked: " + response.GetPayloadAsJson());
                }
                else
                {
                    ConsoleHelper.WriteRedMessage("Direct method failed: " + response.GetPayloadAsJson());
                }
            }
            catch
            {
                ConsoleHelper.WriteRedMessage("Direct method failed: timed-out");
            }
        }

        // INSERT Device twins section below here
        private static async Task SetTwinProperties()
        {
            var twin = await registryManager.GetTwinAsync(deviceId);
            var patch =
                @"{
                tags: {
                    customerID: 'Customer1',
                    cheeseCave: 'CheeseCave1'
                },
                properties: {
                    desired: {
                        patchId: 'set values',
                        temperature: '50',
                        humidity: '85'
                    }
                }
            }";
            await registryManager.UpdateTwinAsync(twin.DeviceId, patch, twin.ETag);

            var query = registryManager.CreateQuery(
                "SELECT * FROM devices WHERE tags.cheeseCave = 'CheeseCave1'", 100);
            var twinsInCheeseCave1 = await query.GetNextAsTwinAsync();
            Console.WriteLine("Devices in CheeseCave1: {0}",
                string.Join(", ", twinsInCheeseCave1.Select(t => t.DeviceId)));
        }
    }

    internal static class ConsoleHelper
    {
        internal static void WriteColorMessage(string text, ConsoleColor clr)
        {
            Console.ForegroundColor = clr;
            Console.WriteLine(text);
            Console.ResetColor();
        }
        internal static void WriteGreenMessage(string text)
        {
            WriteColorMessage(text, ConsoleColor.Green);
        }

        internal static void WriteRedMessage(string text)
        {
            WriteColorMessage(text, ConsoleColor.Red);
        }
    }
}