#!/bin/bash

# Check if a directory is provided as an argument
if [ -z "$1" ]; then
  echo "Usage: $0 <directory>"
  exit 1
fi

# Directory to search
directory=$1

# Initialize an empty string for the output
output=""

# Find Mach-O and Universal Binary files in the specified directory
find "$directory" -type f -exec sh -c '
  for file do
    if file "$file" | grep -qE "Mach-O|universal binary"; then
      if [ -n "$output" ]; then
        output="${output},"
      fi
      output="${output}\"$file\""
    fi
  done
' sh {} +

# Print the output
echo $output
