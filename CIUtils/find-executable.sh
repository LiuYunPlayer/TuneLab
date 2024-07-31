#!/bin/bash

# Get the directory of the current script
script_dir=$(dirname "$0")

# Detect the operating system
os_type=$(uname)

# Execute the corresponding script based on the operating system
case "$os_type" in
  Linux)
    echo $("$script_dir/find-elf.sh" "$1")
    ;;
  Darwin)
    echo $("$script_dir/find-macho.sh" "$1")
    ;;
  *)
    echo "Unsupported OS: $os_type"
    exit 1
    ;;
esac
