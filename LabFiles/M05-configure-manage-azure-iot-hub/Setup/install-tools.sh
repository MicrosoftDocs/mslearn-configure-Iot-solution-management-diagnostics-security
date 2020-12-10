#!/bin/bash

# ensure we are in the user home
pushd ~
# create certificates directory
if [ -d "certificates/" ]
then
    rm -rf certificates
fi

mkdir ~/certificates

# navigate to certificates directory
pushd certificates

# download helper script files
curl https://raw.githubusercontent.com/Azure/azure-iot-sdk-c/master/tools/CACertificates/certGen.sh --output certGen.sh
curl https://raw.githubusercontent.com/Azure/azure-iot-sdk-c/master/tools/CACertificates/openssl_device_intermediate_ca.cnf --output openssl_device_intermediate_ca.cnf
curl https://raw.githubusercontent.com/Azure/azure-iot-sdk-c/master/tools/CACertificates/openssl_root_ca.cnf --output openssl_root_ca.cnf

# update script permissions so user can read, write, and execute it
chmod 700 certGen.sh

# return to original directory
popd
popd
