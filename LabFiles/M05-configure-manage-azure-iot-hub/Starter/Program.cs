// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;

namespace X509CertificateSimulatedDevice
{
    class Program
    {
        // Azure Device Provisioning Service (DPS) Global Device Endpoint
        private const string GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";

        // Azure Device Provisioning Service (DPS) ID Scope
        private static string dpsIdScope = "<DPS-ID-Scope>";

        // Certificate (PFX) File Name
        private static string[] certificateFileNames = new string[]
        {
            "sensor-thl-2001.cert.pfx",
            "sensor-thl-2002.cert.pfx",
            "sensor-thl-2003.cert.pfx",
            "sensor-thl-2004.cert.pfx",
            "sensor-thl-2005.cert.pfx",
            "sensor-thl-2006.cert.pfx",
            "sensor-thl-2007.cert.pfx",
            "sensor-thl-2008.cert.pfx",
            "sensor-thl-2009.cert.pfx",
        };

        // Certificate (PFX) Password
        private static string certificatePassword = "1234";

        // NOTE: For the purposes of this example, the certificatePassword is
        // hard coded. In a production device, the password will need to be stored
        // in a more secure manner. Additionally, the certificate file (PFX) should
        // be stored securely on a production device using a Hardware Security Module.

        public static async Task<int> Main(string[] args)
        {
            var tasks = new List<Task>();

            foreach (var fileName in certificateFileNames)
            {
                X509Certificate2 certificate = LoadProvisioningCertificate(fileName);

                using (var security = new SecurityProviderX509Certificate(certificate))
                {
                    using (var transport = new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly))
                    {
                        ProvisioningDeviceClient provClient =
                            ProvisioningDeviceClient.Create(GlobalDeviceEndpoint, dpsIdScope, security, transport);


                        var container = new ContainerDeviceSimulator(provClient, security);
                        tasks.Add(container.RunAsync());
                        await Task.Delay(30000); // add a device every 30 seconds
                    }
                }
            }

            await Task.WhenAll(tasks);

            return 0;
        }

        private static X509Certificate2 LoadProvisioningCertificate(string certFileName)
        {
            var certificateCollection = new X509Certificate2Collection();
            certificateCollection.Import(certFileName, certificatePassword, X509KeyStorageFlags.UserKeySet);

            X509Certificate2 certificate = null;

            foreach (X509Certificate2 element in certificateCollection)
            {
                Console.WriteLine($"Found certificate: {element?.Thumbprint} {element?.Subject}; PrivateKey: {element?.HasPrivateKey}");
                if (certificate == null && element.HasPrivateKey)
                {
                    certificate = element;
                }
                else
                {
                    element.Dispose();
                }
            }

            if (certificate == null)
            {
                throw new FileNotFoundException($"{certFileName} did not contain any certificate with a private key.");
            }

            Console.WriteLine($"Using certificate {certificate.Thumbprint} {certificate.Subject}");
            return certificate;
        }
    }


    // The ContainerDeviceSimulator class contains the device logic to read from the
    // simulated Device Sensors, and send Device-to-Cloud messages to the Azure IoT
    // Hub. It also contains the code that updates the device with changes to the
    // Device Twin "telemetryDelay" Desired Property.
    public class ContainerDeviceSimulator
    {
        #region Constructor

        readonly ProvisioningDeviceClient provClient;
        readonly SecurityProvider security;
        DeviceClient iotClient;
        string deviceId;

        // Delay between Telemetry readings in Seconds (default to 1 second)
        private int telemetryDelay = 1;

        public ContainerDeviceSimulator(ProvisioningDeviceClient provisioningDeviceClient, SecurityProvider security)
        {
            provClient = provisioningDeviceClient;
            this.security = security;
        }

        #endregion

        public async Task RunAsync()
        {
            Console.WriteLine($"RegistrationID = {security.GetRegistrationID()}");

            // Register the Device with DPS
            Console.Write("ProvisioningClient RegisterAsync . . . ");
            DeviceRegistrationResult result = await provClient.RegisterAsync().ConfigureAwait(false);

            Console.WriteLine($"Device Registration Status: {result.Status}");
            Console.WriteLine($"ProvisioningClient AssignedHub: {result.AssignedHub}; DeviceID: {result.DeviceId}");
            deviceId = result.DeviceId;

            // Verify Device Registration Status
            if (result.Status != ProvisioningRegistrationStatusType.Assigned)
            {
                throw new Exception($"DeviceRegistrationResult.Status is NOT 'Assigned'");
            }

            // Create x509 DeviceClient Authentication
            Console.WriteLine("Creating X509 DeviceClient authentication.");
            var auth = new DeviceAuthenticationWithX509Certificate(result.DeviceId, (security as SecurityProviderX509).GetAuthenticationCertificate());


            Console.WriteLine("Simulated Device. Ctrl-C to exit.");
            using (iotClient = DeviceClient.Create(result.AssignedHub, auth, TransportType.Amqp))
            {
                // Explicitly open DeviceClient to communicate with Azure IoT Hub
                Console.WriteLine("DeviceClient OpenAsync.");
                await iotClient.OpenAsync().ConfigureAwait(false);


                // TODO 1: Setup OnDesiredPropertyChanged Event Handling to receive Desired Properties changes
                Console.WriteLine("Connecting SetDesiredPropertyUpdateCallbackAsync event handler...");
                await iotClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null).ConfigureAwait(false);


                // TODO 2: Load Device Twin Properties since device is just starting up
                Console.WriteLine("Loading Device Twin Properties...");
                var twin = await iotClient.GetTwinAsync().ConfigureAwait(false);
                // Use OnDesiredPropertyChanged event handler to set the loaded Device Twin Properties (re-use!)
                await OnDesiredPropertyChanged(twin.Properties.Desired, null);


                // Start reading and sending device telemetry
                Console.WriteLine("Start reading and sending device telemetry...");
                await SendDeviceToCloudMessagesAsync(iotClient);

                // Explicitly close DeviceClient
                Console.WriteLine("DeviceClient CloseAsync.");
                await iotClient.CloseAsync().ConfigureAwait(false);
            }
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Desired Twin Property Changed:");
            Console.WriteLine($"{desiredProperties.ToJson()}");

            // Read the desired Twin Properties
            if (desiredProperties.Contains("telemetryDelay"))
            {
                string desiredTelemetryDelay = desiredProperties["telemetryDelay"];
                if (desiredTelemetryDelay != null)
                {
                    this.telemetryDelay = int.Parse(desiredTelemetryDelay);
                }
                // if desired telemetryDelay is null or unspecified, don't change it
            }


            // Report Twin Properties
            var reportedProperties = new TwinCollection();
            reportedProperties["telemetryDelay"] = this.telemetryDelay;
            await iotClient.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
            Console.WriteLine("Reported Twin Properties:");
            Console.WriteLine($"{reportedProperties.ToJson()}");
        }

        private async Task SendDeviceToCloudMessagesAsync(DeviceClient deviceClient)
        {
            var sensor = new EnvironmentSensor();

            while (true)
            {
                var currentTemperature = sensor.ReadTemperature();
                var currentHumidity = sensor.ReadHumidity();
                var currentPressure = sensor.ReadPressure();
                var currentLocation = sensor.ReadLocation();

                var messageString = CreateMessageString(currentTemperature,
                                                        currentHumidity,
                                                        currentPressure,
                                                        currentLocation);

                var message = new Message(Encoding.ASCII.GetBytes(messageString));

                // Add a custom application property to the message.
                // An IoT hub can filter on these properties without access to the message body.
                message.Properties.Add("temperatureAlert", (currentTemperature > 30) ? "true" : "false");

                // Send the telemetry message
                await deviceClient.SendEventAsync(message);
                Console.WriteLine("{0} > Sending message: {1}", DateTime.Now, messageString);

                // Delay before next Telemetry reading
                await Task.Delay(telemetryDelay * 1000);
            }
        }

        private static string CreateMessageString(double temperature, double humidity, double pressure, EnvironmentSensor.Location location)
        {
            // Create an anonymous object that matches the data structure we wish to send
            var telemetryDataPoint = new
            {
                temperature = temperature,
                humidity = humidity,
                pressure = pressure,
                latitude = location.Latitude,
                longitude = location.Longitude
            };
            var messageString = JsonConvert.SerializeObject(telemetryDataPoint);

            // Create a JSON string from the anonymous object
            return JsonConvert.SerializeObject(telemetryDataPoint);
        }
    }

    internal class EnvironmentSensor
    {
        // Initial telemetry values
        double minTemperature = 20;
        double minHumidity = 60;
        double minPressure = 1013.25;
        double minLatitude = 39.810492;
        double minLongitude = -98.556061;
        Random rand = new Random();

        internal class Location
        {
            internal double Latitude;
            internal double Longitude;
        }

        internal double ReadTemperature()
        {
            return minTemperature + rand.NextDouble() * 15;
        }
        internal double ReadHumidity()
        {
            return minHumidity + rand.NextDouble() * 20;
        }
        internal double ReadPressure()
        {
            return minPressure + rand.NextDouble() * 12;
        }
        internal Location ReadLocation()
        {
            return new Location { Latitude = minLatitude + rand.NextDouble() * 0.5, Longitude = minLongitude + rand.NextDouble() * 0.5 };
        }
    }
}
