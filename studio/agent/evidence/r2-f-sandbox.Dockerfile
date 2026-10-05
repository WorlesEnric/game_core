FROM public.ecr.aws/ubuntu/ubuntu:24.04
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends python3 ca-certificates dotnet-sdk-8.0 libgtk-3-0t64 libnss3 libasound2t64 libgbm1 libglu1-mesa libgl1 libxcursor1 libxrandr2 libxinerama1 libxi6 libxss1 && rm -rf /var/lib/apt/lists/*
