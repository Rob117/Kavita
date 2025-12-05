FROM mcr.microsoft.com/dotnet/sdk:8.0

# Install Node.js 24 (LTS)
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
    && apt-get install -y nodejs \
    && npm install -g npm@latest

WORKDIR /src
