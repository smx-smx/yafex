FROM mcr.microsoft.com/dotnet/sdk:9.0

# apt-get install -y build-essential gcc g++ cmake libcapstone-dev pkg-config p7zip-full 
RUN apt-get update \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* \
	&& dotnet nuget locals all --clear
    # reserved for cache eviction: 0
