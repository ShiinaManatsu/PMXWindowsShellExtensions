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
	float4 index : TEXCOORD2;
};

cbuffer CBUFFER : register(b0)
{
	float4x4 worldViewProj;
	float4 index;
};

Texture2D albedoTex;
SamplerState albedoTex_Sampler = sampler_state
{
	Texture = albedoTex;
};

PS_IN VS(VS_IN input)
{
	PS_IN output = (PS_IN)0;
	output.pos = mul(float4(input.pos, 1), worldViewProj);
	output.normal = input.normal;
	output.uv = input.vertex;
	output.index = index;
	output.index.x *= 0.000001;
	return output;
}

float4 PS(PS_IN input, out float depth : SV_Depth) : SV_Target
{
	depth = input.pos.z - input.index.x;
	float4 color = albedoTex.Sample(albedoTex_Sampler,input.uv);
	if (input.index.y > 0.5)
	{
		color = color.bgra;
	}
	return color.bgra;	//	Remap color so we can save to bitmap correctly
}