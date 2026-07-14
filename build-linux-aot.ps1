# Build the Docker image which performs the Native AOT build
docker build -t server-latency-build -f ServerLatency/Dockerfile .

# Create a container from the image (don't start it, just create)
docker create --name temp-build-container server-latency-build

# Copy the compiled Linux executable from the container to the host
docker cp temp-build-container:/app/ServerLatency ./ServerLatency-linux-x64

# Clean up
docker rm temp-build-container

echo "Build complete. Linux executable is at ./ServerLatency-linux-x64"
