echo "Provide the path to the The Message From Deep Space";
read -e -p "" path;

if [ ! -f "$path/The Message From Deep Space.exe" ]; then
  echo "Wrong";
  exit -1;
fi

cat > TPFDS.props << EOF
<Project>
  <PropertyGroup>
    <TMFDS_Root>$path</TMFDS_Root>
    <TMFDS_Libs>$path/The Message From Deep Space_Data/Managed</TMFDS_Libs>
  </PropertyGroup>
</Project>
EOF
