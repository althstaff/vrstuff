// Create the shadow map
if( FAILED( g_pd3dDevice->CreateTexture( SHADOW_MAP_SIZE, SHADOW_MAP_SIZE, 1,
					D3DUSAGE_RENDERTARGET, D3DFMT_R32F,
					D3DPOOL_DEFAULT, &g_pShadowMap,
					NULL ) ) )
{
   MessageBox( g_hWnd, "Unable to create shadow map!",
			   "Error", MB_OK | MB_ICONERROR );
   return E_FAIL;
}

// Grab the texture's surface
g_pShadowMap->GetSurfaceLevel( 0, &g_pShadowSurf );

// Ordinary view matrix
D3DXMatrixLookAtLH( &matView, &vLightPos, &vLightAim, &g_vUp );
// Projection matrix for the light
D3DXMatrixPerspectiveFovLH( &matProj, D3DXToRadian(30.0f), 1.0f, 1.0f, 1024.0f );
// Concatenate the world matrix with the above two to get the required matrix
matLightViewProj = matWorld * matView * matProj;

// Shadow generation vertex shader
struct VSOUTPUT_SHADOW
{
   float4 vPosition	: POSITION;
   float  fDepth	   : TEXCOORD0;
};

VSOUTPUT_SHADOW VS_Shadow( float4 inPosition : POSITION )
{
   // Output struct
   VSOUTPUT_SHADOW OUT = (VSOUTPUT_SHADOW)0;
   // Output the transformed position
   OUT.vPosition = mul( inPosition, g_matLightViewProj );
   // Output the scene depth
   OUT.fDepth = OUT.vPosition.z;
   return OUT;
}

float4  PS_Shadow( VSOUTPUT_SHADOW IN ) : COLOR0
{
   // Output the scene depth
   return float4( IN.fDepth, IN.fDepth, IN.fDepth, 1.0f );
}

// Create the screen-sized buffer map
if( FAILED( g_pd3dDevice->CreateTexture( SCREEN_WIDTH, SCREEN_HEIGHT, 1,
			D3DUSAGE_RENDERTARGET, D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT, &g_pScreenMap, NULL ) ) )
{
   MessageBox( g_hWnd, "Unable to create screen map!",
			   "Error", MB_OK | MB_ICONERROR );
   return E_FAIL;
}
// Grab the texture's surface
g_pScreenMap->GetSurfaceLevel( 0, & g_pScreenSurf );

// Generate the texture matrix
float fTexOffs = 0.5 + (0.5 / (float)SHADOW_MAP_SIZE);
D3DXMATRIX matTexAdj( 0.5f,	 0.0f,	 0.0f, 0.0f,
					  0.0f,	 -0.5f,	0.0f, 0.0f,
					  0.0f,	 0.0f,	 1.0f, 0.0f,
					  fTexOffs, fTexOffs, 0.0f, 1.0f );

matTexture = matLightViewProj * matTexAdj;

// Shadow mapping vertex shader
struct VSOUTPUT_UNLIT
{
   float4 vPosition   : POSITION;
   float4 vTexCoord   : TEXCOORD0;
   float  fDepth	  : TEXCOORD1;
};

VSOUTPUT_UNLIT VS_Unlit( float4 inPosition : POSITION )
{
   // Output struct
   VSOUTPUT_UNLIT OUT = (VSOUTPUT_UNLIT)0;

   // Output the transformed position
   OUT.vPosition = mul( inPosition, g_matWorldViewProj );

   // Output the projective texture coordinates
   OUT.vTexCoord = mul( inPosition, g_matTexture );

   // Output the scene depth
   OUT.fDepth = mul( inPosition, g_matLightViewProj ).z;

   return OUT;
}

// Shadow mapping pixel shader
float4  PS_Unlit( VSOUTPUT_UNLIT IN ) : COLOR0
{
   // Generate the 9 texture co-ordinates for a 3x3 PCF kernel
   float4 vTexCoords[9];
   // Texel size
   float fTexelSize = 1.0f / 1024.0f;

   // Generate the tecture co-ordinates for the specified depth-map size
   // 4 3 5
   // 1 0 2
   // 7 6 8
   vTexCoords[0] = IN.vTexCoord;
   vTexCoords[1] = IN.vTexCoord + float4( -fTexelSize, 0.0f, 0.0f, 0.0f );
   vTexCoords[2] = IN.vTexCoord + float4(  fTexelSize, 0.0f, 0.0f, 0.0f );
   vTexCoords[3] = IN.vTexCoord + float4( 0.0f, -fTexelSize, 0.0f, 0.0f );
   vTexCoords[6] = IN.vTexCoord + float4( 0.0f,  fTexelSize, 0.0f, 0.0f );
   vTexCoords[4] = IN.vTexCoord + float4( -fTexelSize, -fTexelSize, 0.0f, 0.0f );
   vTexCoords[5] = IN.vTexCoord + float4(  fTexelSize, -fTexelSize, 0.0f, 0.0f );
   vTexCoords[7] = IN.vTexCoord + float4( -fTexelSize,  fTexelSize, 0.0f, 0.0f );
   vTexCoords[8] = IN.vTexCoord + float4(  fTexelSize,  fTexelSize, 0.0f, 0.0f );
   // Sample each of them checking whether the pixel under test is shadowed or not
   float fShadowTerms[9];
   float fShadowTerm = 0.0f;
   for( int i = 0; i < 9; i++ )
   {
	  float A = tex2Dproj( ShadowSampler, vTexCoords[i] ).r;
	  float B = (IN.fDepth – 0.1f);

	  // Texel is shadowed
	  fShadowTerms[i] = A < B ? 0.0f : 1.0f;
	  fShadowTerm	 += fShadowTerms[i];
   }
   // Get the average
   fShadowTerm /= 9.0f;
   return fShadowTerm;
}

// Create the blur maps
for( int i = 0; i < 2; i++ )
{
   if( FAILED( g_pd3dDevice->CreateTexture( SCREEN_WIDTH, SCREEN_HEIGHT, 1,
											D3DUSAGE_RENDERTARGET,
											D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT,
											&g_pBlurMap[i], NULL ) ) )
   {
	  MessageBox( g_hWnd, "Unable to create blur map!",
				  "Error", MB_OK | MB_ICONERROR );
	  return E_FAIL;
   }
  // Grab the texture's surface
   g_pBlurMap[i]->GetSurfaceLevel( 0, & g_pBlurSurf[i] );
}

float GetGaussianDistribution( float x, float y, float rho )
{
   float g = 1.0f / sqrt( 2.0f * 3.141592654f * rho * rho );
   return g * exp( -(x * x + y * y) / (2 * rho * rho) );
}

void GetGaussianOffsets( bool bHorizontal, D3DXVECTOR2 vViewportTexelSize,
						 D3DXVECTOR2* vSampleOffsets, float* fSampleWeights )
{
   // Get the center texel offset and weight
   fSampleWeights[0] = 1.0f * GetGaussianDistribution( 0, 0, 2.0f );
   vSampleOffsets[0] = D3DXVECTOR2( 0.0f, 0.0f );
   // Get the offsets and weights for the remaining taps
   if( bHorizontal )
   {
	  for( int i = 1; i < 15; i += 2 )
	  {
		 vSampleOffsets[i + 0] = D3DXVECTOR2( i * vViewportTexelSize.x, 0.0f );
		 vSampleOffsets[i + 1] = D3DXVECTOR2( -i * vViewportTexelSize.x, 0.0f );
		 fSampleWeights[i + 0] = 2.0f * GetGaussianDistribution( float(i + 0), 0.0f, 3.0f );
		 fSampleWeights[i + 1] = 2.0f * GetGaussianDistribution( float(i + 1), 0.0f, 3.0f );
	  }
   }
   else
   {
	  for( int i = 1; i < 15; i += 2 )
	  {
		 vSampleOffsets[i + 0] = D3DXVECTOR2( 0.0f, i * vViewportTexelSize.y );
		 vSampleOffsets[i + 1] = D3DXVECTOR2( 0.0f, -i * vViewportTexelSize.y );
		 fSampleWeights[i + 0] = 2.0f * GetGaussianDistribution( 0.0f, float(i + 0), 3.0f );
		 fSampleWeights[i + 1] = 2.0f * GetGaussianDistribution( 0.0f, float(i + 1), 3.0f );
	  }
   }
}

// Gaussian filter vertex shader
struct VSOUTPUT_BLUR
{
   float4 vPosition	: POSITION;
   float2 vTexCoord	: TEXCOORD0;
};

VSOUTPUT_BLUR VS_Blur( float4 inPosition : POSITION, float2 inTexCoord : TEXCOORD0 )
{
   // Output struct
   VSOUTPUT_BLUR OUT = (VSOUTPUT_BLUR)0;
   // Output the position
   OUT.vPosition = inPosition;
   // Output the texture coordinates
   OUT.vTexCoord = inTexCoord;
   return OUT;
}
// Horizontal blur pixel shader
float4 PS_BlurH( VSOUTPUT_BLUR IN ): COLOR0
{
   // Accumulated color
   float4 vAccum = float4( 0.0f, 0.0f, 0.0f, 0.0f );
   // Sample the taps (g_vSampleOffsets holds the texel offsets
   // and g_fSampleWeights holds the texel weights)
   for(int i = 0; i < 15; i++ )
   {
	  vAccum += tex2D( ScreenSampler, IN.vTexCoord + g_vSampleOffsets[i] ) * g_fSampleWeights[i];
   }
   return vAccum;
}
// Vertical blur pixel shader
float4 PS_BlurV( VSOUTPUT_BLUR IN ): COLOR0
{
   // Accumulated color
   float4 vAccum = float4( 0.0f, 0.0f, 0.0f, 0.0f );
   // Sample the taps (g_vSampleOffsets holds the texel offsets and
   // g_fSampleWeights holds the texel weights)
   for( int i = 0; i < 15; i++ )
   {
	  vAccum += tex2D( BlurHSampler, IN.vTexCoord + g_vSampleOffsets[i] ) * g_fSampleWeights[i];
   }
   return vAccum;
}

struct VSOUTPUT_SCENE
{
   float4 vPosition	  : POSITION;
   float2 vTexCoord	  : TEXCOORD0;
   float4 vProjCoord	 : TEXCOORD1;
   float4 vScreenCoord   : TEXCOORD2;
   float3 vNormal		: TEXCOORD3;
   float3 vLightVec	  : TEXCOORD4;
   float3 vEyeVec		: TEXCOORD5;
};
// Scene vertex shader
VSOUTPUT_SCENE VS_Scene( float4 inPosition : POSITION, float3 inNormal : NORMAL,
						 float2 inTexCoord : TEXCOORD0 )
{
   VSOUTPUT_SCENE OUT = (VSOUTPUT_SCENE)0;
   // Output the transformed position
   OUT.vPosition = mul( inPosition, g_matWorldViewProj );
   // Output the texture coordinates
   OUT.vTexCoord = inTexCoord;
   // Output the projective texture coordinates
   // (we use this to project the spot texture down onto the scene)
   OUT.vProjCoord = mul( inPosition, g_matTexture );
   // Output the screen-space texture coordinates
   OUT.vScreenCoord.x = ( OUT.vPosition.x * 0.5 + OUT.vPosition.w * 0.5 );
   OUT.vScreenCoord.y = ( OUT.vPosition.w * 0.5 - OUT.vPosition.y * 0.5 );
   OUT.vScreenCoord.z = OUT.vPosition.w;
   OUT.vScreenCoord.w = OUT.vPosition.w;
   // Get the world space vertex position
   float4 vWorldPos = mul( inPosition, g_matWorld );
   // Output the world space normal
   OUT.vNormal = mul( inNormal, g_matWorldIT );
   // Move the light vector into tangent space
   OUT.vLightVec = g_vLightPos.xyz - vWorldPos.xyz;
   // Move the eye vector into tangent space
   OUT.vEyeVec = g_vEyePos.xyz - vWorldPos.xyz;
   return OUT;
}

float4 PS_Scene( VSOUTPUT_SCENE IN ) : COLOR0
{
   // Normalize the normal, light and eye vectors
   IN.vNormal   = normalize( IN.vNormal );
   IN.vLightVec = normalize( IN.vLightVec );
   IN.vEyeVec   = normalize( IN.vEyeVec );
   // Sample the color and normal maps
   float4 vColor  = tex2D( ColorSampler, IN.vTexCoord );
   // Compute the ambient, diffuse and specular lighting terms
   float ambient  = 0.0f;
   float diffuse  = max( dot( IN.vNormal, IN.vLightVec ), 0 );
   float specular = pow(max(dot( 2 * dot( IN.vNormal, IN.vLightVec ) * IN.vNormal
								 - IN.vLightVec, IN.vEyeVec ), 0 ), 8 );
   if( diffuse == 0 ) specular = 0;
   // Grab the shadow term
   float fShadowTerm = tex2Dproj( BlurVSampler, IN.vScreenCoord );
   // Grab the spot term
   float fSpotTerm = tex2Dproj( SpotSampler, IN.vProjCoord );
   // Compute the final color
   return (ambient * vColor) +
		  (diffuse * vColor * g_vLightColor * fShadowTerm * fSpotTerm) +
		  (specular * vColor * g_vLightColor.a * fShadowTerm * fSpotTerm);
}
