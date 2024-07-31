#!/bin/bash

# Check if a directory is provided as an argument
if [ -z "$1" ]; then
  echo "Usage: $0 <directory>"
  exit 1
fi

# Directory to search
directory=$1

# Find ELF files in the specified directory
files=$(find "$directory" -type f -exec sh -c 'file -b "$1" | grep -q "ELF" && echo "$1"' _ {} \;)

# Initialize an empty string for the output
output=""

# Loop through each file and format it
for file in $files; do
  if [ -z "$output" ]; then
    output="\"$file\""
  else
    output="$output,\"$file\""
  fi
done

# Print the formatted output
echo $output
