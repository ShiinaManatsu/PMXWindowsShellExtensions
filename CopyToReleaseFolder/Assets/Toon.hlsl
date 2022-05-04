struct VS_IN
{
	float3 pos : POSITION;
	float3 normal : NORMAL;
	float2 vertex : TEXCOORD;
};

struct PS_IN
{
	float4 pos : SV_POSITION;
	float3 normal : TEXCOORD0;
	float2 uv : TEXCOORD1;
	float3 viewDir : TEXCOORD2;
	float3 viewNormal : TEXCOORD3;
};

cbuffer CBUFFER : register(b0)
{
	float4x4 matrix_V;
	float4x4 worldViewProj;
	float4 cameraPosition;
	float4 renderParams;	//	x: Drawing index, y: Swap RB, z:shininess, w: (Sphere operation DISBLE = 0,MULT = 1,ADD = 2,SUB_TEXTURE = 3)
	float4 diffuse;
	float4 specularColor;
	float4 ambientColor;
	float4 textureSwapRBFlag;	//	x: albedo, y: sph, z: toon
};

Texture2D albedoTex;
SamplerState albedoTex_Sampler = sampler_state{	Texture = albedoTex; };

Texture2D sphTex;
SamplerState sphTex_Sampler = sampler_state{ Texture = sphTex; };

Texture2D toonTex;
SamplerState toonTex_Sampler = sampler_state{ Texture = toonTex; };

PS_IN VS(VS_IN input)
{
	PS_IN output = (PS_IN)0;
	output.pos = mul(float4(input.pos, 1), worldViewProj);
	output.viewDir = normalize(input.pos - cameraPosition.xyz);
	output.viewNormal = normalize( mul( matrix_V, float4(normalize(input.normal), 0.0) ).xyz );
	output.normal = input.normal;
	output.uv = input.vertex;
	return output;
}

float4 PS(PS_IN input, out float depth : SV_Depth) : SV_Target
{
	float offset = renderParams.x * 0.000001;
	depth = input.pos.z - offset;

	float3 normal = normalize(input.normal);
	float3 viewDir = input.viewDir;
	float shininess = renderParams.z;
	float3 lightColor = 1;
	float3 lightDir = normalize(float3(-0.41,-0.82,0.41));

	// Specular
	float specularStrength = 1;
	float dirDotNormalHalf = max(0, dot(normal, normalize(lightDir + viewDir)));
	float dirSpecularWeight = pow( dirDotNormalHalf, shininess );
	float3 dirSpecular = specularColor.xyz * lightColor * dirSpecularWeight;
	
	// ToonMap
	float lightStrength = dot(lightDir, normal) * 0.5 + 0.5;
	float4 toon = toonTex.Sample(toonTex_Sampler, float2( specularStrength, lightStrength ));
	if(textureSwapRBFlag.z == 1)
	{
		toon = toon.bgra;
	}
	
	// Sphere Map
	float3 viewNormal = -input.viewNormal;
	float2 sphereUv = viewNormal.xy * 0.5 + 0.5;
	float3 sph = sphTex.Sample(sphTex_Sampler, sphereUv).rgb;
	if(textureSwapRBFlag.y == 1)
	{
		sph = sph.bgr;
	}

	float4 col = albedoTex.Sample(albedoTex_Sampler,input.uv);
	if(textureSwapRBFlag.x == 1)
	{
		col = col.bgra;
	}

	if(renderParams.w == 1)
	{
		col.rgb *= sph;
	}
	else if(renderParams.w == 2)
	{
		col.rgb += sph;
	}
	else if(renderParams.w == 3)
	{
		col.rgb -= sph;
	}

	// Light
	float3 light = saturate( ambientColor.xyz + ( diffuse.rgb * lightColor ) );
	light *= col.rgb;
	light += saturate(dirSpecular);
	light *= toon.xyz;
	
	float4 final = float4(light,col.a * diffuse.a);
	return lerp(final, final.bgra, renderParams.y);	//	Swap RB channel so we can save to bitmap correctly
}